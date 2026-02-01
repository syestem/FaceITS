1. Скачать: https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip

2. Поместить в папку с сервером (новая пустая папка где вам удобно), запустить

3. Запустить steamcmd.exe, прописать в консоль, по очереди:
Логин под анонимным аккакнутом:
login anonymous
Скачивание сервера:
app_update 730 validate
Пойдет процесс установки, после него:
quit

4. Создаем ярлык для запуска сервера со следующим содержанием:
cd *ВАШ ПУТЬ ДО ПАПКИ С STEAMCMD*\steamapps\common\Counter-Strike Global Offensive\game\bin\win64
Например:
cd steamcmd\steamapps\common\Counter-Strike Global Offensive\game\bin\win64
start cs2.exe -dedicated -usercon -console -secure -dev +game_type 0 +game_mode 1 +sv_logfile 1 -serverlogging -tickrate 128 +sv_setsteamaccount STEAM_KEY +map de_mirage
Надпись "STEAM_KEY" нужно заменить на ключ на сайте: https://steamcommunity.com/dev/managegameservers
Здесь в самом низу страницы нужно создать ключ для сервера. В первом поле пишем 730 (числовой номер CS2 в библиотеке Steam), второе поле можно оставить пустым.

5. Создаем ярлык для обновления игры:
cd steamcmd
steamcmd.exe +login anonymous +app_update 730 validate +quit

6. Установка конфигов:
6.1 Скачиваем архив по ссылке: https://github.com/syestem/FaceITS/tree/cfg
6.2 Переносим все .cfg файлы в папку steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg

7. Установка MMSource:
7.1 Скачиваем по ссылке самую последнюю версию: https://www.metamodsource.net/downloads.php?branch=dev
7.2 Папку addons помещаем в \steamapps\common\Counter-Strike Global Offensive\game\csgo
7.3 В файле этой же папке в файле gameinfo.gi:
После строки "Game_LowViolence	csgo_lv // Perfect World content override" добавляем:
Game	csgo/addons/metamod
7.4 После запуска и загрузки сервера, пропишите "meta version" без кавычек в консоли, чтобы убедиться, что MMSource исправно установлен

8. Установка CounterStrikeSharp:
8.1 Скачиваем по ссылке последний релиз: https://github.com/roflmuffin/CounterStrikeSharp/releases/
8.2 Релиз должен быть с припиской "with-runtime", в случае, если ставите впервые
8.3 В архиве папку addons сливаем с прошлой папкой с заменой
8.4 Проверяем командой "css_plugins list" без кавычек, что все установилось

9. Установка плагина FaceITS:
9.1 Скачиваем по ссылке: https://github.com/syestem/FaceITS/releases
9.2 Сливаем с папкой addons
9.3 Конфиг плагина в папке csgo\addons\counterstrikesharp\configs\plugins\FaceITS, файл "config.json"

10. Установка Weapon Paints:
10.1 Скачиваем последний релиз плагина MenuManagerCS2: https://github.com/NickFox007/MenuManagerCS2/releases
10.2 Плагин нужен для того, чтобы показывалась менюшка выбора скина. Установка - слияние папок addons
10.3 Скачиваем последний релиз плагина PlayerSettings: https://github.com/NickFox007/PlayerSettingsCS2/releases
10.4 Плагин нужен для сохранений настроек игрока. Установка - слияние папок addons
10.5 Скачиваем последний релиз плагина AnyBaseLibCS2: https://github.com/NickFox007/AnyBaseLibCS2/releases
10.6 Плагин используется в качестве библиотеки. Установка - слияние папок addons
10.7 После установок трех дополнительных плагинов, рекомендуется запустить игру и проверить с помощью команды "css_plugins list", что все 3 плагина установились. Последнего (AnyBaseLibCS2) может в списке не быть, это не критично
10.8 Скачиваем последний релиз плагина Weapon Paints: https://github.com/Nereziel/cs2-WeaponPaints/releases
10.9 Добрались до основного плагина. Установка - разместить в game/csgo/addons/counterstrikesharp/plugins папку WeaponPaints, а в game/csgo/addons/counterstrikesharp папку gamedata
10.10 В папке addons/counterstrikesharp/configs открыть файл core.json и изменить параметр FollowCS2ServerGuidelines на false
10.11 Запустить сервер для генерации конфиг-файла (addons/counterstrikesharp/configs/plugins/WeaponPaints/WeaponPaints.json)
10.12 Настройка плагина и веб версии хорошо описаны в этих видео: https://youtu.be/MJqhm-j7uYI?si=mCKWzfrjSUCkj7BZ и https://youtu.be/mFKBDarsHH0?si=367cNlHALb_JTsVS (всё абсолютно бесплатно)
10.13 Готовый конфиг можно скачать по ссылке: https://disk.yandex.ru/d/DO7Ait209rFaFA. Установка - слияние папок addons

0. Полезные команды:
0.1 Смена имен (названий) команд:
"mp_teamname_1 ?" и "mp_teamname_2 ?"
0.2 Старт тех паузы:
"mp_pause_match"
0.3 Стоп тех паузы:
"mp_unpause_match"
0.4 Бэкап раунда:
"mp_backup_restore_load_file backup_round??.txt" - где вопросы, подставить номер раунда
0.5 Список плагинов:
"css_plugins list"
0.6 Полный перезапуск плагина:
"css_plugins restart НАЗВАНИЕ"