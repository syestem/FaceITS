using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json;
using CSTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

public class FaceITS : BasePlugin
{
    public override string ModuleName => "FaceITS";
    public override string ModuleVersion => "5.1.0";
    public override string ModuleAuthor => "Terentev Alex";

    private string _knifeWinnerTeamName = "";
    private string _knifeWinnerCaptainName = "";

    private MatchConfig _config = null!;

    private bool _waitingForReady;
    private bool _knifeStage;
    private bool _knifeFinished;
    private bool _warmupStarted;
    private bool _roundEndHandled;
    private bool _firstRoundPending;
    private readonly HashSet<ulong> _readyCaptains = new();
    private CCSPlayerController? _knifeWinner;

    private readonly Dictionary<ulong, Dictionary<ulong, int>> _damageMatrix = new();
    private readonly Dictionary<ulong, Dictionary<ulong, int>> _hitMatrix = new();

    private readonly List<CSTimer> _stageTimers = new();
    private readonly List<CSTimer> _stageCommandTimers = new();

    private const string GREEN = "\x01\x04";
    private const string ZW = "\u200B";

    public enum MatchStage
    {
        Warmup,
        Ready1,
        BothReady,
        KnifeRound,
        KnifeChoice,
        KnifeChoiceStay,
        KnifeChoiceSwitch,
        FirstRound,
        EveryRoundStart,
        EveryRoundEnd
    }

    public override void Load(bool hotReload)
    {
        LoadConfig();

        AddCommandListener("say", OnChat);
        AddCommandListener("say_team", OnChat);

        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        FullReset();
    }

    private void FullReset()
    {
        _waitingForReady = true;
        _knifeStage = false;
        _knifeFinished = false;
        _warmupStarted = false;
        _roundEndHandled = false;

        _readyCaptains.Clear();
        _knifeWinner = null;

        _hitMatrix.Clear();
        _damageMatrix.Clear();

        foreach (var t in _stageCommandTimers)
            t.Kill();

        _stageCommandTimers.Clear();
        KillStageTimers();
    }

    private void KillStageTimers()
    {
        foreach (var t in _stageTimers)
            t.Kill();
        _stageTimers.Clear();
    }

    private void LoadConfig()
    {
        var dir = Path.Combine(Server.GameDirectory,
            "csgo/addons/counterstrikesharp/configs/plugins/FaceITS");

        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "FaceITS.json");

        if (!File.Exists(path))
                {
            File.WriteAllText(path, GetDefaultConfig());
            Console.WriteLine("[FaceITS] Config not found. Generated default FaceITS.json");
        }

        var raw = JsonSerializer.Deserialize<RawConfig>(File.ReadAllText(path))!;

        _config = new MatchConfig
                {
            Texts = raw.Texts,
            Commands = new MatchCommands
                    {
                CaptainSteamIds = raw.Commands.CaptainSteamIds.Select(ulong.Parse).ToList(),
                ReadyCommands = raw.Commands.ReadyCommands.Select(x => x.ToLower()).ToList(),
                TechCommand = raw.Commands.TechCommand.ToLower(),
                StayCommands = raw.Commands.StayCommands.Select(x => x.ToLower()).ToList(),
                SwitchCommands = raw.Commands.SwitchCommands.Select(x => x.ToLower()).ToList(),
            }
        };
    }


    private void Chat(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        foreach (var raw in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            Server.PrintToChatAll($"{GREEN}{ZW}{_config.Texts.ChatPrefix}{GREEN}{ZW}{line}");
        }
    }

    private void RunStage(MatchStage stage)
    {
        Console.WriteLine($"[FaceITS] STAGE ------> {stage}");
        if (stage == MatchStage.BothReady ||
            stage == MatchStage.KnifeChoiceStay ||
            stage == MatchStage.KnifeChoiceSwitch ||
            stage == MatchStage.FirstRound) {
            KillStageTimers();
        }

        if (stage == MatchStage.EveryRoundEnd)
            PrintDamageTable();

        foreach (var msg in _config.Texts.StageMessages.Where(x => x.Stage == stage.ToString())) {
            if (string.IsNullOrWhiteSpace(msg.Text)) continue;

            if (msg.Interval < 0)
                _stageTimers.Add(AddTimer(msg.Delay, () => Chat(ApplyVars(msg.Text))));
            else
                _stageTimers.Add(AddTimer(msg.Delay, () =>
                        {
                    Chat(ApplyVars(msg.Text));
                    _stageTimers.Add(AddTimer(msg.Interval, () => Chat(ApplyVars(msg.Text)), TimerFlags.REPEAT));
                }));
        }

        foreach (var cmd in _config.Texts.StageCommands.Where(x => x.Stage == stage.ToString()))
                {
            if (string.IsNullOrWhiteSpace(cmd.Command)) continue;

            if (cmd.Interval < 0)
                _stageCommandTimers.Add(AddTimer(cmd.Delay, () => Server.ExecuteCommand($"{ApplyVars(cmd.Command)};")));
            else
                _stageCommandTimers.Add(AddTimer(cmd.Delay, () =>
                        {
                    Server.ExecuteCommand($"{ApplyVars(cmd.Command)};");
                    _stageCommandTimers.Add(AddTimer(cmd.Interval,
                        () => Server.ExecuteCommand($"{ApplyVars(cmd.Command)};"),
                        TimerFlags.REPEAT));
                }));
        }
    }

    private string ApplyVars(string text)
    {
        var ct = ConVar.Find("mp_teamname_1")?.StringValue ?? "CT";
        var t = ConVar.Find("mp_teamname_2")?.StringValue ?? "T";
        var score = GetTeamScore();

        return text
            .Replace("{CT}", ct)
            .Replace("{T}", t)
            .Replace("{CTSCORE}", score.ct.ToString())
            .Replace("{TSCORE}", score.t.ToString())
            .Replace("{KNIFE_CHOICE_TEAM_WINNER}", _knifeWinnerTeamName)
            .Replace("{KNIFE_CHOICE_CAPTAIN_WINNER}", _knifeWinnerCaptainName);
    }

    private (int ct, int t) GetTeamScore()
    {
        int ct = 0, t = 0;
        foreach (var team in Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager"))
                {
            if (team.TeamNum == (int)CsTeam.CounterTerrorist) ct = team.Score;
            else if (team.TeamNum == (int)CsTeam.Terrorist) t = team.Score;
        }
        return (ct, t);
    }

    private void PrintDamageTable()
    {
        var players = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.AuthorizedSteamID != null && !p.IsBot && !p.IsHLTV)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_config.Texts.DamageHeader))
                {
            var team1 = ConVar.Find("mp_teamname_1")?.StringValue ?? "CT";
            var team2 = ConVar.Find("mp_teamname_2")?.StringValue ?? "T";
            var score = GetTeamScore();

            Chat(_config.Texts.DamageHeader
                .Replace("{TEAM1}", team1)
                .Replace("{TEAM2}", team2)
                .Replace("{CT}", score.ct.ToString())
                .Replace("{T}", score.t.ToString()));
        }

        foreach (var me in players)
                {
            ulong myId = me.AuthorizedSteamID!.SteamId64;

            foreach (var enemy in players.Where(p => p.Team != me.Team))
                    {
                ulong enemyId = enemy.AuthorizedSteamID!.SteamId64;

                int dealt = 0;
                int taken = 0;
                int dealtHits = 0;
                int takenHits = 0;

                if (_damageMatrix.TryGetValue(myId, out var vs))
                        {
                    dealt = vs.GetValueOrDefault(enemyId);
                    if (_hitMatrix.TryGetValue(myId, out var hv))
                        dealtHits = hv.GetValueOrDefault(enemyId);
                }

                if (_damageMatrix.TryGetValue(enemyId, out var from))
                        {
                    taken = from.GetValueOrDefault(myId);
                    if (_hitMatrix.TryGetValue(enemyId, out var hf))
                        takenHits = hf.GetValueOrDefault(myId);
                }


                dealt = Math.Clamp(dealt, 0, 100);
                taken = Math.Clamp(taken, 0, 100);

                int hp = enemy.PlayerPawn?.Value?.Health ?? 0;
                hp = Math.Clamp(hp, 0, 100);

                var name = enemy.PlayerName;
                if (string.IsNullOrWhiteSpace(name) || name.Contains("*"))
                    name = $"Player_{enemyId}";

                me.PrintToChat($"{GREEN}{ZW}{_config.Texts.ChatPrefix}{GREEN}{ZW}" +
                    _config.Texts.DamageRow
                        .Replace("{NAME}", name)
                        .Replace("{DEALT}", dealt.ToString())
                        .Replace("{DEALTHITS}", dealtHits.ToString())
                        .Replace("{TAKEN}", taken.ToString())
                        .Replace("{TAKENHITS}", takenHits.ToString())
                        .Replace("{HP}", hp.ToString()));

            }
        }
    }


    private HookResult OnChat(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || command.ArgCount < 2)
            return HookResult.Continue;

        var auth = player.AuthorizedSteamID;
        if (auth == null) return HookResult.Continue;

        string cmd = command.ArgByIndex(1).Trim().ToLower();
        ulong steam64 = auth.SteamId64;

        if (cmd == _config.Commands.TechCommand)
                {
            Server.ExecuteCommand("mp_pause_match");
            return HookResult.Handled;
        }

        if (_waitingForReady &&
            _config.Commands.CaptainSteamIds.Contains(steam64) &&
            _config.Commands.ReadyCommands.Contains(cmd))
                {
            if (_readyCaptains.Add(steam64))
                    {
                Console.WriteLine($"[FaceITS] READY COUNT = {_readyCaptains.Count}");

                if (_readyCaptains.Count == 1)
                        {
                    RunStage(MatchStage.Ready1);
                }
                else if (_readyCaptains.Count == 2)
                        {
                    RunStage(MatchStage.BothReady);
                    StartKnifeRound();
                }
            }
            return HookResult.Handled;
        }

        if (_knifeStage && _knifeWinner == player)
                {
            // classic !stay / !switch
            if (_config.Commands.StayCommands.Contains(cmd))
                    {
                FinishKnife(true);
                return HookResult.Handled;
            }

            if (_config.Commands.SwitchCommands.Contains(cmd))
                    {
                FinishKnife(false);
                return HookResult.Handled;
            }

            // !t / !ct logic
            if (cmd == "!t" || cmd == "!ct")
                    {
                bool captainIsT = player.Team == CsTeam.Terrorist;
                bool chooseT = cmd == "!t";

                bool stay =
                    captainIsT
                        ? chooseT       // T captain: !t = stay
                        : !chooseT;     // CT captain: !ct = stay

                FinishKnife(stay);
                return HookResult.Handled;
            }
        }


        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt ev, GameEventInfo info)
    {
        var victim = ev.Userid;
        var attacker = ev.Attacker;

        if (victim == null || attacker == null ||
            !victim.IsValid || !attacker.IsValid ||
            victim.AuthorizedSteamID == null ||
            attacker.AuthorizedSteamID == null)
            return HookResult.Continue;

        ulong victimId = victim.AuthorizedSteamID.SteamId64;
        ulong attackerId = attacker.AuthorizedSteamID.SteamId64;

        int dmg = ev.DmgHealth;

        // DAMAGE
        if (!_damageMatrix.ContainsKey(attackerId))
            _damageMatrix[attackerId] = new Dictionary<ulong, int>();

        _damageMatrix[attackerId][victimId] =
            _damageMatrix[attackerId].GetValueOrDefault(victimId) + dmg;

        // HITS
        if (!_hitMatrix.ContainsKey(attackerId))
            _hitMatrix[attackerId] = new Dictionary<ulong, int>();

        _hitMatrix[attackerId][victimId] =
            _hitMatrix[attackerId].GetValueOrDefault(victimId) + 1;

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart ev, GameEventInfo info)
    {
        _roundEndHandled = false;

        if (!_warmupStarted)
                {
            _warmupStarted = true;
            RunStage(MatchStage.Warmup);
        }

        _damageMatrix.Clear();
        _hitMatrix.Clear();

        if (_knifeFinished && !_firstRoundPending)
            RunStage(MatchStage.EveryRoundStart);

        if (_firstRoundPending)
                {
            _firstRoundPending = false;
            RunStage(MatchStage.FirstRound);
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd ev, GameEventInfo info)
    {
        if (_roundEndHandled) return HookResult.Continue;
        _roundEndHandled = true;

        if (_knifeStage)
                {
            if (_knifeWinner != null)
                return HookResult.Continue;

            foreach (var p in Utilities.GetPlayers())
                    {
                if (!p.IsValid || p.AuthorizedSteamID == null) continue;

                if (p.Team == (CsTeam)ev.Winner &&
                    _config.Commands.CaptainSteamIds.Contains(p.AuthorizedSteamID.SteamId64))
                        {
                    _knifeWinner = p;
                    _knifeWinnerCaptainName = p.PlayerName;
                    _knifeWinnerTeamName = p.Team == CsTeam.CounterTerrorist
                        ? (ConVar.Find("mp_teamname_1")?.StringValue ?? "CT")
                        : (ConVar.Find("mp_teamname_2")?.StringValue ?? "T");

                    RunStage(MatchStage.KnifeChoice);
                    break;
                }
            }
        }

        RunStage(MatchStage.EveryRoundEnd);
        return HookResult.Continue;
    }

    private void StartKnifeRound()
    {
        _waitingForReady = false;
        _knifeStage = true;
        RunStage(MatchStage.KnifeRound);
    }

    private void FinishKnife(bool stay)
    {
        Console.WriteLine($"[FaceITS] FinishKnife stay={stay}");

        _knifeStage = false;

        if (stay)
            RunStage(MatchStage.KnifeChoiceStay);
        else
            RunStage(MatchStage.KnifeChoiceSwitch);

        _knifeFinished = true;
        _firstRoundPending = true;
    }
    private string GetDefaultConfig()
    {
        return """
          {
          "_comment_stage_help_1": "============================================================",
          "_comment_stage_help_2": " FACEITS PLUGIN CONFIG (Stage system)",
          "_comment_stage_help_3": " interval = -1 → вывод один раз",
          "_comment_stage_help_4": " delay = задержка перед первым выводом",
          "_comment_stage_help_5": " команды выполняются через StageCommands",
          "_comment_stage_help_6": " multiline через \\n",
          "_comment_stage_help_7": "============================================================",

          "Texts": {
            "StageMessages": [
              {
                 "Stage": "Warmup",
                 "Text": "[ЦРС^] Доступные команды для капитана:\n- !tech — техническая пауза\n- !r / !ready — готовность",
                 "Delay": 0,
                 "Interval": 25
              },

              {
                "Stage": "Ready1",
                "Text": "[ЦРС^] Готовность капитанов: [1/2]",
                "Delay": 0,
                "Interval": -1
              },

              {
                "Stage": "BothReady",
                "Text": "[ЦРС^] Готовность капитанов: [2/2]",
                "Delay": 0,
                "Interval": -1
              },

              {
                "Stage": "BothReady",
                "Text": "[ЦРС^] Матч начнётся через 15 секунд!",
                "Delay": 1,
                "Interval": -1
              },

              {
                "Stage": "BothReady",
                "Text": "[ЦРС^] KNIFE!\n[ЦРС^] KNIFE!\n[ЦРС^] KNIFE!",
                "Delay": 15,
                "Interval": -1
              },

              {
                "Stage": "KnifeChoice",
                "Text": "Команда {KNIFE_CHOICE_TEAM_WINNER} победила ножевой раунд, {KNIFE_CHOICE_CAPTAIN_WINNER} нужно прописать !stay или !switch",
                "Delay": 1,
                "Interval": 15
              },

              {
                "Stage": "KnifeChoiceStay",
                "Text": "Команда {KNIFE_CHOICE_TEAM_WINNER} решила остаться на своей стороне!",
                "Delay": 0,
                "Interval": -1
              },

              {
                "Stage": "KnifeChoiceSwitch",
                "Text": "Команда {KNIFE_CHOICE_TEAM_WINNER} решила перейти на другую сторону!",
                "Delay": 0,
                "Interval": -1
              },

              {
                "Stage": "FirstRound",
                "Text": "[ЦРС^] LIVE!\n[ЦРС^] LIVE!\n[ЦРС^] LIVE!",
                "Delay": 23,
                "Interval": -1
              },

              {
                "Stage": "FirstRound",
                "Text": "[ЦРС^] Пожалуйста, имейте в виду, что во всех матчах включены овертаймы, ничьи в соревновательной игре отсутствуют.",
                "Delay": 15,
                "Interval": -1
              },

              {
                "Stage": "EveryRoundStart",
                "Text": "[ЦРС^] КВЕРТИ — киберспорт начинается здесь!",
                "Delay": 0,
                "Interval": -1
              }
            ],

            "StageCommands": [
              {
                "Stage": "BothReady",
                "Command": "mp_warmuptime 17",
                "Delay": 1,
                "Interval": -1
              },

              {
                "Stage": "BothReady",
                "Command": "mp_warmuptime_all_players_connected 17",
                "Delay": 1,
                "Interval": -1
              },

              {
                "Stage": "BothReady",
                "Command": "exec start_knife",
                "Delay": 15,
                "Interval": -1
              }, 

              {
                "Stage": "BothReady",
                "Command": "mp_warmup_end",
                "Delay": 17,
                "Interval": -1
              },

              {
                "Stage": "KnifeChoice",
                "Command": "mp_pause_match",
                "Delay": 0,
                "Interval": -1
              },

              {
                "Stage": "KnifeChoiceStay",
                "Command": "exec knife_stay",
                "Delay": 1,
                "Interval": -1
              },

              {
                "Stage": "KnifeChoiceSwitch",
                "Command": "exec knife_switch",
                "Delay": 1,
                "Interval": -1
              }
            ],

            "DamageHeader": "[ЦРС^] {TEAM1} [{CT} - {T}] {TEAM2}",
            "DamageRow": "[ЦРС^] НАНЕС: [{DEALT} / {DEALTHITS} hits] ПОЛУЧИЛ: [{TAKEN} / {TAKENHITS} hits] - {NAME} [{HP} hp]",

            "ChatPrefix": ""
          },

          "Commands": {
            "CaptainSteamIds": [
              "76561199188043710",
              "76561198208281409"
            ],

            "ReadyCommands": ["!r", "!ready"],
            "TechCommand": "!tech",

            "StayCommands": ["!stay"],
            "SwitchCommands": ["!switch"]
          }
        }  
        """;
    }
}

// ================= MODELS =================

public class StageMessage
{
    public string Stage { get; set; } = "";
    public string Text { get; set; } = "";
    public float Delay { get; set; } = 0f;
    public float Interval { get; set; } = -1f;
}

public class StageCommand
{
    public string Stage { get; set; } = "";
    public string Command { get; set; } = "";
    public float Delay { get; set; } = 0f;
    public float Interval { get; set; } = -1f;
}

public class RawConfig
{
    public TextBlock Texts { get; set; } = new();
    public CommandBlock Commands { get; set; } = new();
}

public class MatchConfig
{
    public TextBlock Texts { get; set; } = new();
    public MatchCommands Commands { get; set; } = new();
}

public class TextBlock
{
    public List<StageMessage> StageMessages { get; set; } = new();
    public List<StageCommand> StageCommands { get; set; } = new();
    public string DamageHeader { get; set; } = "";
    public string DamageRow { get; set; } = "";
    public string ChatPrefix { get; set; } = "";
}

public class CommandBlock
{
    public List<string> CaptainSteamIds { get; set; } = new();
    public List<string> ReadyCommands { get; set; } = new();
    public string TechCommand { get; set; } = "";
    public List<string> StayCommands { get; set; } = new();
    public List<string> SwitchCommands { get; set; } = new();
}

public class MatchCommands : CommandBlock
{
    public new List<ulong> CaptainSteamIds { get; set; } = new();
}
