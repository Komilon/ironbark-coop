# Ironbark Coop

**Two-player online co-op for _Fears to Fathom: Ironbark Lookout_, in the style of the series' own online episode, _Scratch Creek_.**
**Кооператив на двоих для _Fears to Fathom: Ironbark Lookout_ по образцу онлайн-части серии, _Scratch Creek_.**

[English](#english) · [Русский](#русский) · [Changelog / Изменения](CHANGELOG.md) · [Releases / Релизы](../../releases)

> Unofficial fan mod. Not affiliated with Fears to Fathom or its developers. The repository and the releases contain **no files of either game**: models, sounds and animations are read at runtime from the player's own installed copies of Ironbark Lookout and Scratch Creek.
>
> Неофициальный фанатский мод, не связан с разработчиками Fears to Fathom. В репозитории и релизах **нет файлов игр**: модели, звуки и анимации мод читает во время игры из ваших установленных копий Ironbark Lookout и Scratch Creek.

---

## English

You both play one story together, each in your own body, with your own flashlight and inventory. Either of you can move the story forward; a scare only gets the one who ran into it.

### Requirements

- **Fears to Fathom: Ironbark Lookout** for both players, **the same version**. The installer prints a "game build code"; yours and your friend's must match.
- **Fears to Fathom: Scratch Creek** for both players, to see each other as Marcus and Tessa. Without it the partner looks like the park ranger.
- Windows x64.
- A shared network: LAN, a virtual LAN (**Radmin VPN**, ZeroTier, Tailscale) or a forwarded port on the router.

### Install

1. Download `IronbarkCoop-<version>.zip` from [Releases](../../releases) and unpack it anywhere.
2. Close the game and run `install.bat`. It finds the game folder by itself (Steam libraries) or asks for the path.
3. Compare the "game build code" with your friend.
4. On first start the mod spends a few seconds reading the characters from your Scratch Creek and caches them in `BepInEx\cache`. Nothing is downloaded.

Uninstall: `uninstall.bat` (it asks before removing BepInEx, which other mods may use).

### Playing together

1. Main menu → **Multiplayer**.
2. The **host** presses **Create game** and gets a **code for your friend** (`IB-XXXXX-XXXXX`); send it (the **Copy code** button helps).
3. The **friend** pastes the code (or the host's IP) and presses **Connect**.
4. When both are in, the host presses **Choose chapter** and starts a new game or continues a save. The friend follows into the same scene and night.
5. If Windows asks about the firewall, allow access. Port **25061**, TCP and UDP.

The code carries an address you can be reached at: external (if UPnP worked), else Radmin/ZeroTier/Tailscale, else LAN. If the connection drops, just connect again: the game catches you up to the current night and world state.

**Auto port (UPnP, experimental)** — a switch in the Multiplayer menu that asks the router to open the port so you can play without a VPN.

### Controls

- **V** (hold) — voice. Voice activation or another key can be set in the config.
- **Ctrl** (hold) — crouch, as in Scratch Creek.
- **Left click on the passenger seat** — sit next to the driver; **Space** — stand up.
- **Left click on an answer** in your partner's radio talk — your vote; **Space** — step away from the talk.
- **F8** — connection panel.
- Everything else as in the game.

### What it feels like

- **Your partner is Marcus or Tessa from Scratch Creek** (chosen in the Multiplayer menu; "auto" makes the host Marcus). Scratch Creek animations: idle, walk, run, crouch, crouch-walk, sitting, sleeping. The head turns before the body; flashlight and items are held with IK.
- **3D proximity voice**: full near you, fading out by about 12 m, muffled behind walls; Scratch Creek's speaking icon over the talker. Voice and movement go over UDP.
- **Shared story**: doors, lights, generator, stove, blinds, items and night progress match for both, and either player can do the tasks. **Personal inventory** and **personal scares**; a death respawns only that player in the tower.
- **Diner**: fixed sides (Marcus on the bench where food is served, Tessa across); the waitress brings a tray to each of you.
- **Rides**: driver and passenger seat, or stand in the cab; the ride's shake is felt inside; wheel, engine, horn and radio are shared.
- **Tower**: one desk chair, one computer (booted once, windows shared, screen shown on the monitor); radio talks seen by both and answered together (coloured dots show who picked what: Marcus amber, Tessa teal).
- **Going to sleep, Scratch Creek style**: whoever lies down first waits on their half of the bed; the night ends when both are in bed.
- **Night 2, Scratch Creek style**: Tessa wakes up for the night; Marcus dreams — he walks a dark, blurred forest with a flashlight and hears Tessa far away, with heavy reverb.
- **Co-op chores** on calm evenings (nights 4 and 6): the stove goes out, the wind blows the blinds open.

### Configuration

`BepInEx\config\astra.ironbark.coop.cfg`

| Section / key | What it does |
|---|---|
| Character / Survivor | `auto`, `marcus` or `tessa`. |
| Character / ScratchCreekPath | Scratch Creek data folder, if it was not found automatically. |
| Session / PlayerName | Name over your character; empty — Steam or Windows name. |
| Session / Code | Session code for connecting by IP (the same for both). |
| Network / UPnP | Experimental automatic port opening. |
| Voice / Mode, PushToTalkKey, ActivationThreshold, PartnerVolume, Microphone | Voice settings. |

### What is in the release zip

| Path | What it is |
|---|---|
| `install.bat`, `install.ps1` | Installer: finds the game, copies `game\*` into it, prints the game build code. |
| `uninstall.bat`, `uninstall.ps1` | Removes the mod and its config; BepInEx only if you agree. |
| `game\winhttp.dll`, `game\doorstop_config.ini`, `game\.doorstop_version`, `game\changelog.txt`, `game\BepInEx\core\*` | BepInEx 5.4.23.5 x64 mod loader, official files, unmodified. |
| `game\BepInEx\plugins\IronbarkCoop\IronbarkCoop.dll` | The mod. |
| `game\BepInEx\plugins\IronbarkCoop\Concentus.dll` | Opus voice codec (managed). |
| `game\BepInEx\plugins\IronbarkCoop\AssetsTools.NET.dll`, `classdata.tpk` | Reads Scratch Creek's characters and animations from your install. |
| `game\BepInEx\plugins\IronbarkCoop\System.*.dll` | .NET support libraries for the above. |
| `licenses\` | This mod's licence, third-party licences and a SHA-256 manifest of every file. |
| `README.md`, `CHANGELOG.md` | This text and the change history. |

### Troubleshooting

- The log is `BepInEx\LogOutput.log` in the game folder; lines from the mod start with `[Ironbark Cooperative]`.
- "Version mismatch" — both must have the same game build code and the same mod version.
- Cannot connect — try the host's Radmin VPN/ZeroTier address, allow the game in the Windows firewall, or forward port 25061 (TCP+UDP).
- Partner looks like the ranger — Scratch Creek was not found: set `Character / ScratchCreekPath`.

### Status

Tested automatically on two real copies of the game on one PC (20 test groups: network with packet loss and TCP fallback, menu and join code, avatars and animations, voice, shared story, personal scares and respawn, chores, every campaign scene, continue and reconnect, rides, diner, tower, nights 1–3 played end to end). Human playtests on one PC so far.

**Not yet verified:** a session over the internet between two PCs, a live microphone, a full campaign played by people, UPnP on a real router, the Steam connection path.

**Known limitations:** only the waitress and the ranger are synced, other NPCs are per player (for personal scares); getting in and out of vehicles has no animation; the partner's view of the computer screen is a small picture updated twice a second.

### Building from source

Requires the .NET SDK (6+), a copy of the game and an unpacked [BepInEx 5.4.23.5 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).

```
dotnet build src -c Release -p:GameDir="<game folder>" -p:LoaderDir="<BepInEx folder>\BepInEx\core"
powershell -File tools\make-release.ps1 -Version <x.y.z> -LoaderDir "<BepInEx folder>"
```

The build references the game's own assemblies from your install; none of them are in this repository. `tools\make-release.ps1` refuses to package any game file, decompiled source, `.pdb`, image or cache.

### Licence and credits

- Mod source code: [MIT](LICENSE).
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) with HarmonyX, MonoMod, Mono.Cecil (MIT); [Concentus](https://github.com/lostromb/concentus) (BSD-3-Clause); [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) and `classdata.tpk` from [UABEA](https://github.com/nesrak1/UABEA) (MIT); .NET System libraries (MIT). Details in [licenses/THIRD-PARTY.txt](licenses/THIRD-PARTY.txt).
- Fears to Fathom, Ironbark Lookout and Scratch Creek belong to their owners.
- Made with the help of an AI coding assistant (Claude, by Anthropic) under the author's direction and playtesting.

---

## Русский

Вы оба проходите одну историю вместе, каждый в своём теле, со своим фонариком и инвентарём. Двигать сюжет может любой из вас, а напугать игра может только того, кто сам наткнулся на скример.

### Что нужно

- **Fears to Fathom: Ironbark Lookout** у обоих, **одной версии**. Установщик покажет «код версии игры», и у вас с другом он должен совпасть.
- **Fears to Fathom: Scratch Creek** у обоих, если хотите видеть друг друга как Маркуса и Тессу. Без неё партнёр выглядит как смотритель парка.
- Windows x64.
- Одна сеть: локальная, виртуальная (**Radmin VPN**, ZeroTier, Tailscale) или открытый порт на роутере.

### Установка

1. Скачайте `IronbarkCoop-<версия>.zip` в разделе [Releases](../../releases) и распакуйте куда угодно.
2. Закройте игру и запустите `install.bat`. Он найдёт папку игры сам (библиотеки Steam) или спросит путь.
3. Сравните с другом «код версии игры».
4. При первом запуске мод за несколько секунд прочитает персонажей из вашей Scratch Creek и сохранит их в `BepInEx\cache`. Скачивать ничего не нужно.

Удаление: `uninstall.bat` (BepInEx удаляется только с вашего согласия: им могут пользоваться другие моды).

### Как играть вместе

1. В главном меню выберите **«Мультиплеер»**.
2. **Хост** нажимает **«Создать игру»** и видит **«Код для друга»** (вида `IB-XXXXX-XXXXX`). Отправьте код другу, удобнее всего кнопкой «Скопировать код».
3. **Друг** вставляет код (или IP хоста) в поле и нажимает **«Подключиться»**.
4. Когда оба на месте, хост нажимает **«Выбрать главу»** и начинает новую игру или продолжает сохранение. Друг попадает в ту же сцену и ночь автоматически.
5. Если Windows спросит про брандмауэр, разрешите доступ. Порт **25061**, TCP и UDP.

Код содержит адрес, по которому до вас можно достучаться: внешний (если сработал UPnP), иначе Radmin/ZeroTier/Tailscale, иначе локальной сети. Если оборвётся связь, просто подключитесь снова: игра догонит вас до текущей ночи и состояния мира.

**Авто-порт UPnP (эксперимент)** — переключатель в меню мультиплеера. Он просит роутер открыть порт, чтобы играть без VPN. Работает только с роутерами, где UPnP включён.

### Управление

- **V** (удерживать) — голос. В настройках можно включить активацию голосом или сменить клавишу.
- **Ctrl** (удерживать) — присесть, как в Scratch Creek. Отпустили — встали (если над головой есть место).
- **ЛКМ по пассажирскому сиденью** — сесть рядом с водителем, **Пробел** — встать.
- **ЛКМ по варианту ответа** в разговоре партнёра по рации — ваш голос за ответ, **Пробел** — отойти от разговора.
- **F8** — панель соединения во время игры.
- Остальное — как в игре.

### Как это ощущается в игре

- **Партнёр — Маркус или Тесса из Scratch Creek.** Персонаж выбирается в меню мультиплеера («Персонаж: авто / Маркус / Тесса»); в режиме «авто» хост — Маркус, гость — Тесса. Анимации из Scratch Creek: стойка, ходьба, бег, присед, ходьба вприсядку, сидение, сон. Голова поворачивается раньше тела, фонарик и предметы в руках держатся через IK.
- **Голос в 3D:** в полную силу вблизи, дальше плавно затихает и пропадает примерно через 12 м, за стенами глуше. Над говорящим иконка речи из Scratch Creek. Голос и движение идут по UDP и не «заикаются» при потерях в сети.
- **Сидящих и спящих видно и слышно:** в машине, в кабинке закусочной, за компьютером — голова сидящего там, где его камера. На стул у компьютера садится один (и через клик по монитору тоже): второму скажут «Это место занято».
- **Спать ложатся вдвоём, как в Scratch Creek:** кто лёг первым, ждёт на своей половине кровати (Пробел — встать), ночь кончается, когда ляжет второй. Каждый спит на своей половине, партнёр лежит в позе сна из Scratch Creek.
- **Вторая ночь, как в Scratch Creek:** ночью просыпается Тесса (рация в кровати, туалет, снова спать), а Маркус спит и видит сон: идёт с фонариком по тёмному лесу, картинка сначала сильно размыта и проясняется на первых шагах, слышен звук сна, Тессу он слышит издалека, с эхом. Для Тессы Маркус спит рядом. Когда Тесса ложится, оба просыпаются утром; утром с кофе каждый стоит у перил на своём месте.
- **Места в закусочной закреплены:** Маркус всегда садится на свою сторону стола (куда приносят еду), Тесса — напротив. Если Маркус уже сидит, клик Тессы по любому столу сажает её напротив него.
- **Еда в закусочной:** каждому свой поднос. Официантка приносит поднос Маркусу, потом возвращается за вторым для Тессы; пока его не принесли, Маркус ждёт. Каждый ест только со своего подноса. С момента заказа до оплаты счёта за столом заказа не встаёт никто из тех, кто за ним сидит (как в одиночной игре).
- **Сумка одна на двоих** во всех сценах: если её несёт партнёр, вас не остановят словами «надо забрать сумку». Положенная в фургон сумка едет вместе с ним и не мешает ему ехать. Ключ от вышки появляется у обоих.
- **Что в руках, видно:** дрова, брошенные фонарики лежат у обоих (два брошенных — два фонарика, поднять может любой), зажжённые спички, спрей от медведей и канистра видны и слышны партнёру.
- **Компьютер на вышке один:** включают его один раз — второй сразу попадает на рабочий стол; пароль, обновление и открытые окна общие, на мониторе виден экран того, кто сейчас работает.
- **Разговоры по рации вдвоём:** если вы на вышке, видите разговор партнёра (и слова собеседника, и ответы) и выбираете ответ вместе. Ответ засчитывается, когда оба выбрали одно и то же; чей выбор где — показывают точки цвета персонажа (Маркус — янтарная, Тесса — бирюзовая). Отошли или вышли из вышки — разговор идёт без вас.
- **Скример в будке охранника** достаётся тому, кто вошёл первым; рейнджер потом ждёт Маркуса и заговаривает, когда тот подходит.
- **Люди вокруг ведут Маркуса:** барменша в закусочной и рейнджер на старте тропы работают с Маркусом, Тесса видит всё то же самое и может сама поболтать с барменшей. Поход в туалет в закусочной — задача Тессы, еду принесут, когда она вернётся.
- **Двери, свет, радио и музыка общие:** дверь фургона, туалеты, фары и радио открываются и включаются у обоих, песня играет с одного места.
- **Сюжет общий:** двери, свет, генератор, печь, жалюзи, предметы и прогресс ночи совпадают у обоих, и задания может выполнять любой.
- **Инвентарь у каждого свой:** что взял, то у тебя, пока не положишь.
- **Скримеры личные.** Второй игрок видит, как партнёр вздрагивает, и слышит вскрик, только если стоит рядом.
- **Смерть одиночная:** погибший тихо возрождается в вышке, второй продолжает своё.
- **Вступления, финал и титры** смотрите вместе.
- **Поездки вдвоём:** тряска фургона ощущается и пассажиром, и стоящим в салоне; места независимые — кто сел за руль, тот ведёт, второй садится на пассажирское сиденье или едет стоя в салоне. Руль крутится, двигатель и гудок слышны обоим.
- **Кооп-обязанности** в спокойные вечера (ночи 4 и 6): погасла печь, ветер распахнул жалюзи. Заглохший генератор временно отключён.

### Настройки

Файл `BepInEx\config\astra.ironbark.coop.cfg`.

| Раздел / ключ | Что делает |
|---|---|
| Character / Survivor | `auto`, `marcus` или `tessa`. |
| Character / ScratchCreekPath | Папка данных Scratch Creek, если её не нашло автоматически. |
| Session / PlayerName | Ник над персонажем; пусто — имя Steam или Windows. |
| Session / Code | Код сессии для подключения по IP (у обоих одинаковый). |
| Network / UPnP | Экспериментальное автоматическое открытие порта. |
| Voice / Mode, PushToTalkKey, ActivationThreshold, PartnerVolume, Microphone | Настройки голоса. |

### Что лежит в архиве релиза

| Путь | Что это |
|---|---|
| `install.bat`, `install.ps1` | Установщик: находит игру, копирует в неё `game\*`, показывает код версии игры. |
| `uninstall.bat`, `uninstall.ps1` | Удаляет мод и его настройки; BepInEx — только если согласитесь. |
| `game\winhttp.dll`, `game\doorstop_config.ini`, `game\.doorstop_version`, `game\changelog.txt`, `game\BepInEx\core\*` | Загрузчик модов BepInEx 5.4.23.5 x64, официальные файлы без изменений. |
| `game\BepInEx\plugins\IronbarkCoop\IronbarkCoop.dll` | Сам мод. |
| `game\BepInEx\plugins\IronbarkCoop\Concentus.dll` | Голосовой кодек Opus. |
| `game\BepInEx\plugins\IronbarkCoop\AssetsTools.NET.dll`, `classdata.tpk` | Чтение персонажей и анимаций из вашей установленной Scratch Creek. |
| `game\BepInEx\plugins\IronbarkCoop\System.*.dll` | Вспомогательные библиотеки .NET. |
| `licenses\` | Лицензия мода, лицензии сторонних компонентов и SHA-256 каждого файла. |
| `README.md`, `CHANGELOG.md` | Этот текст и история изменений. |

### Если что-то не так

- Лог — `BepInEx\LogOutput.log` в папке игры; строки мода начинаются с `[Ironbark Cooperative]`.
- «Несовпадение версий» — у обоих должны совпадать код версии игры и версия мода.
- Не подключается — попробуйте адрес хоста в Radmin VPN/ZeroTier, разрешите игру в брандмауэре Windows или откройте порт 25061 (TCP+UDP).
- Партнёр выглядит как рейнджер — Scratch Creek не найдена: укажите `Character / ScratchCreekPath`.

### Что проверено и что нет

Проверено автотестами на двух настоящих копиях игры на одном компьютере (20 групп тестов): сеть с потерями и переход на TCP, меню и код подключения, персонажи и анимации, голос, общий сюжет, личные скримеры и возрождение, обязанности, все сцены кампании, продолжение и переподключение, поездки, закусочная, вышка, ночи 1–3 целиком. Живые плейтесты пока — на одном компьютере.

Ещё **не** проверено: игра через интернет между двумя ПК, живой микрофон, полное прохождение людьми, UPnP на реальном роутере, подключение через Steam.

**Известные ограничения:** синхронизируются только барменша и рейнджер, остальные NPC у каждого свои (ради личных скримеров); посадка в машину и выход без анимации; картинка экрана компьютера у партнёра небольшая и обновляется дважды в секунду.

### Сборка из исходников

Нужны .NET SDK (6+), установленная игра и распакованный [BepInEx 5.4.23.5 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). Команды — в английском разделе выше. Сборка ссылается на библиотеки игры из вашей установки; в репозитории их нет.

### Лицензия и благодарности

- Исходный код мода: [MIT](LICENSE).
- BepInEx (LGPL-2.1), HarmonyX, MonoMod, Mono.Cecil (MIT), Concentus (BSD-3), AssetsTools.NET и `classdata.tpk` из UABEA (MIT), библиотеки .NET (MIT). Подробно: [licenses/THIRD-PARTY.txt](licenses/THIRD-PARTY.txt).
- Fears to Fathom, Ironbark Lookout и Scratch Creek принадлежат их владельцам.
- Сделано с помощью ИИ-ассистента (Claude от Anthropic) под руководством автора и по его плейтестам.
