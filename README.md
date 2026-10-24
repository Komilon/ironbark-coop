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
- **Fears to Fathom: Scratch Creek** — optional. With it you can look like Marcus or Tessa. Without it you pick one of Ironbark's own people (the ranger, the diner waitress, the man in a suit), read from your Ironbark install.
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

- **Pick how you look** in the Multiplayer menu (**Look**): a 3D view you can turn with the mouse, with every pose (standing, walking, running, crouching, sitting, asleep). Choices: Marcus and Tessa (if you have Scratch Creek), the ranger, the waitress and the man in a suit (from Ironbark itself). If your partner lacks your pick (no Scratch Creek), they see you as a similar Ironbark character. **Role** is separate: Marcus (the host in "auto") leads the story.
- **Animations**: Scratch Creek's when you have it (also on Ironbark's people), otherwise Ironbark's own. The head turns before the body; flashlight and items are held with IK.
- **3D proximity voice**: full near you, fading out by about 12 m, muffled behind walls; Scratch Creek's speaking icon over the talker. Voice and movement go over UDP.
- **Shared story**: doors, lights, generator, stove, blinds, items and night progress match for both, and either player can do the tasks. **Personal inventory** and **personal scares**; a death respawns only that player in the tower.
- **Diner**: fixed sides (Marcus on the bench where food is served, Tessa across); the waitress brings a tray to each of you.
- **Rides**: driver and passenger seat, or stand in the cab; the ride's shake is felt inside; wheel, engine, horn and radio are shared.
- **Tower**: one desk chair, one computer (booted once, windows shared, screen shown on the monitor); radio talks seen by both and answered together (coloured dots show who picked what: Marcus amber, Tessa teal).
- **Going to sleep, Scratch Creek style**: whoever lies down first waits on their half of the bed; the night ends when both are in bed.
- **Night 2, Scratch Creek style**: Tessa wakes up for the night; Marcus dreams — he walks a dark, blurred forest with a flashlight. He hears Tessa with heavy reverb as if from where he sleeps: clearly while she is in the tower, fading away as she leaves it; he hears her doors and switches too, and her switching the tower light on floods his dream with light. Asleep, he is not heard by her at all. His opening card shows question marks instead of the time. In the morning Marcus has his coffee at the rail while Tessa is still in bed and gets up when she likes (Space).
- **The trail to the campers' smoke (day 3)**: go together or one alone. Whoever reaches the trail head first waits for the partner, with one answer to go right away instead; together, one bear spray is enough. Whoever stays gets a task list (shut the boards, stoke the stove, restart the generator when it stalls) and then a nap; a partner done at the campfire earlier waits for that nap, and the evening starts for both, the napper waking in bed.
- **Night 4 supper**: Kayla's recipe comes to Tessa; you cook from it together (every ingredient either of you puts in is in the dish for both); once baked, the oven holds two trays, a portion each, and you eat sitting on the bed, one at each end. The evening starts with the stove burning. Before sleeping Marcus has to go down to the porta-potty — and something may need checking on his way back.
- **Night 5, one cultist for both**: the figure at the window, his round on the balcony, the ritual and his way down are the same for both. Scares stay personal: whoever makes him notice them is hunted, and the partner sees him run at them; after a death he is gone for the night and leaves the skull behind.
: the stove goes out, the wind blows the blinds open.

### Configuration

`BepInEx\config\astra.ironbark.coop.cfg`

| Section / key | What it does |
|---|---|
| Character / Survivor | Story role: `auto`, `marcus` or `tessa`. |
| Character / Look | How you look: `auto`, `marcus`, `tessa`, `ib-ranger`, `ib-waitress`, `ib-suitguy` (set from the menu). |
| Character / ScratchCreekPath | Scratch Creek data folder, if it was not found automatically; `none` — don't use it. |
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
| `game\BepInEx\plugins\IronbarkCoop\AssetsTools.NET.dll`, `classdata.tpk` | Reads the characters and animations from your own Ironbark Lookout (and Scratch Creek, if installed). |
| `game\BepInEx\plugins\IronbarkCoop\System.*.dll` | .NET support libraries for the above. |
| `licenses\` | This mod's licence, third-party licences and a SHA-256 manifest of every file. |
| `README.md`, `CHANGELOG.md` | This text and the change history. |

### Troubleshooting

- The log is `BepInEx\LogOutput.log` in the game folder; lines from the mod start with `[Ironbark Cooperative]`.
- "Version mismatch" — both must have the same game build code and the same mod version.
- Cannot connect — try the host's Radmin VPN/ZeroTier address, allow the game in the Windows firewall, or forward port 25061 (TCP+UDP).
- Marcus and Tessa are missing from the Look list — Scratch Creek was not found: set `Character / ScratchCreekPath`.

### Status

Tested automatically on two real copies of the game on one PC (25 test groups: network with packet loss and TCP fallback, menu and join code, avatars and animations, voice, shared story, personal scares and respawn, chores, every campaign scene, continue and reconnect, rides, diner, tower, nights 1–3 played end to end, the trail to the smoke together and alone, night 4's supper and Marcus's errand, night 5's cultist, the look picker, playing without Scratch Creek). Human playtests on one PC so far.

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
- **Fears to Fathom: Scratch Creek** — не обязательна. С ней можно выглядеть как Маркус или Тесса. Без неё выбираете одного из людей самой Ironbark (смотритель, официантка из закусочной, мужчина в костюме) — мод берёт их из вашей установленной Ironbark.
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

- **Внешность выбираете сами** в меню мультиплеера («Внешность»): 3D-просмотр, модель крутится мышью, можно посмотреть все позы (стоит, идёт, бежит, присел, сидит, спит). На выбор: Маркус и Тесса (если есть Scratch Creek), смотритель, официантка и мужчина в костюме (из самой Ironbark). Если у партнёра нет вашего персонажа (нет Scratch Creek), он увидит похожего из Ironbark. **Роль** — отдельная настройка: сюжет ведёт Маркус (в режиме «авто» это хост).
- **Анимации** из Scratch Creek, если она есть (и для людей из Ironbark тоже), иначе — из самой Ironbark. Голова поворачивается раньше тела, фонарик и предметы в руках держатся через IK.
- **Голос в 3D:** в полную силу вблизи, дальше плавно затихает и пропадает примерно через 12 м, за стенами глуше. Над говорящим иконка речи из Scratch Creek. Голос и движение идут по UDP и не «заикаются» при потерях в сети.
- **Сидящих и спящих видно и слышно:** в машине, в кабинке закусочной, за компьютером — голова сидящего там, где его камера. На стул у компьютера садится один (и через клик по монитору тоже): второму скажут «Это место занято».
- **Спать ложатся вдвоём, как в Scratch Creek:** кто лёг первым, ждёт на своей половине кровати (Пробел — встать), ночь кончается, когда ляжет второй. Каждый спит на своей половине, партнёр лежит в позе сна из Scratch Creek.
- **Вторая ночь, как в Scratch Creek:** ночью просыпается Тесса (рация в кровати, туалет, снова спать), а Маркус спит и видит сон: идёт с фонариком по тёмному лесу, картинка сначала сильно размыта и проясняется на первых шагах, слышен звук сна. Тессу он слышит с эхом, как будто с кровати: отчётливо, пока она в вышке, и всё тише, когда она уходит. Её двери и выключатели он тоже слышит, а включённый ею свет в вышке заливает его сон светом. Пока Маркус спит, Тесса его не слышит. Вместо времени в заставке ночи у него знаки вопроса. Для Тессы Маркус спит рядом. Когда Тесса ложится, наступает утро: Маркус стоит у перил с кофе, а Тесса ещё лежит в кровати и встаёт, когда захочет (Пробел).
- **Поход к дыму кемперов (день 3):** можно вдвоём или одному. Кто первым дошёл до тропы, ждёт напарника; единственный ответ в окне — «Идти сразу». Вдвоём хватает одного баллончика от медведей. Кто остался, получает список заданий (закрыть ставни, подтопить печь, завести заглохший генератор) и потом дремлет; если напарник у костра управился раньше, он ждёт, пока оставшийся не ляжет. Вечер начинается у обоих, задремавший просыпается в кровати.
- **Ужин в четвёртую ночь:** рецепт от Кайлы приходит Тессе, готовите по нему вместе (любой ингредиент, положенный кем угодно, лежит в блюде у обоих), после запекания в духовке два подноса, по порции каждому, а едите сидя на кровати, каждый со своего края. Вечер начинается с уже горящей печью. Перед сном Маркусу нужно спуститься в биотуалет — а на обратном пути, возможно, придётся кое-что проверить.
- **Пятая ночь, один культист на двоих:** силуэт в окне, его обход балкона, ритуал и уход вниз — одни и те же у обоих. Испуги личные: на кого он обратил внимание, за тем и гонится, а напарник видит, как он бежит к нему; после гибели он уходит на всю ночь и оставляет череп.
 Маркус всегда садится на свою сторону стола (куда приносят еду), Тесса — напротив. Если Маркус уже сидит, клик Тессы по любому столу сажает её напротив него.
- **Еда в закусочной:** каждому свой поднос. Официантка приносит поднос Маркусу, потом возвращается за вторым для Тессы; пока его не принесли, Маркус ждёт. Каждый ест только со своего подноса. С момента заказа до оплаты счёта за столом заказа не встаёт никто из тех, кто за ним сидит (как в одиночной игре).
- **Сумка одна на двоих** во всех сценах: если её несёт партнёр, вас не остановят словами «надо забрать сумку». Положенная в фургон сумка едет вместе с ним и не мешает ему ехать. Ключ от вышки появляется у обоих.
- **Что в руках, видно:** дрова, брошенные фонарики лежат у обоих (два брошенных — два фонарика, поднять может любой), зажжённые спички, спрей от медведей и канистра видны и слышны партнёру, пролитый бензин виден обоим.
- **Компьютер на вышке один:** включают его один раз — второй сразу попадает на рабочий стол; пароль, обновление и открытые окна общие, на мониторе виден экран того, кто сейчас работает.
- **Разговоры по рации вдвоём:** если вы на вышке, видите разговор партнёра (и слова собеседника, и ответы) и выбираете ответ вместе. Ответ засчитывается, когда оба выбрали одно и то же; чей выбор где — показывают точки цвета персонажа (Маркус — янтарная, Тесса — бирюзовая). Отошли или вышли из вышки — разговор идёт без вас.
- **Скример в будке охранника** достаётся тому, кто вошёл первым; рейнджер потом ждёт Маркуса и заговаривает, когда тот подходит.
- **Люди вокруг ведут Маркуса:** барменша в закусочной и рейнджер на старте тропы работают с Маркусом, Тесса видит всё то же самое и может сама поболтать с барменшей. Поход в туалет в закусочной — задача Тессы, еду принесут, когда она вернётся.
- **Двери, свет, радио и музыка общие:** дверь фургона, туалеты (и биотуалет у вышки), фары и радио открываются и включаются у обоих, песня играет с одного места.
- **Сюжет общий:** двери, свет, генератор, печь, жалюзи, предметы и прогресс ночи совпадают у обоих, и задания может выполнять любой.
- **Инвентарь у каждого свой:** что взял, то у тебя, пока не положишь.
- **Скримеры личные.** Второй игрок видит, как партнёр вздрагивает, и слышит вскрик, только если стоит рядом.
- **Смерть одиночная:** погибший тихо возрождается в вышке, второй продолжает своё.
- **Вступления, финал и титры** смотрите вместе.
- **Поездки вдвоём:** тряска фургона ощущается и пассажиром, и стоящим в салоне; места независимые — кто сел за руль, тот ведёт, второй садится на пассажирское сиденье или едет стоя в салоне. Руль крутится, двигатель и гудок слышны обоим.
- **Кооп-обязанности** в спокойные вечера (ночь 6): погасла печь, ветер распахнул жалюзи. Заглохший генератор временно отключён.

### Настройки

Файл `BepInEx\config\astra.ironbark.coop.cfg`.

| Раздел / ключ | Что делает |
|---|---|
| Character / Survivor | Роль в сюжете: `auto`, `marcus` или `tessa`. |
| Character / Look | Внешность: `auto`, `marcus`, `tessa`, `ib-ranger`, `ib-waitress`, `ib-suitguy` (выбирается в меню). |
| Character / ScratchCreekPath | Папка данных Scratch Creek, если её не нашло автоматически; `none` — не использовать. |
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
| `game\BepInEx\plugins\IronbarkCoop\AssetsTools.NET.dll`, `classdata.tpk` | Чтение персонажей и анимаций из вашей установленной Ironbark Lookout (и Scratch Creek, если она есть). |
| `game\BepInEx\plugins\IronbarkCoop\System.*.dll` | Вспомогательные библиотеки .NET. |
| `licenses\` | Лицензия мода, лицензии сторонних компонентов и SHA-256 каждого файла. |
| `README.md`, `CHANGELOG.md` | Этот текст и история изменений. |

### Если что-то не так

- Лог — `BepInEx\LogOutput.log` в папке игры; строки мода начинаются с `[Ironbark Cooperative]`.
- «Несовпадение версий» — у обоих должны совпадать код версии игры и версия мода.
- Не подключается — попробуйте адрес хоста в Radmin VPN/ZeroTier, разрешите игру в брандмауэре Windows или откройте порт 25061 (TCP+UDP).
- В списке внешностей нет Маркуса и Тессы — Scratch Creek не найдена: укажите `Character / ScratchCreekPath`.

### Что проверено и что нет

Проверено автотестами на двух настоящих копиях игры на одном компьютере (25 групп тестов): сеть с потерями и переход на TCP, меню и код подключения, персонажи и анимации, голос, общий сюжет, личные скримеры и возрождение, обязанности, все сцены кампании, продолжение и переподключение, поездки, закусочная, вышка, ночи 1–3 целиком, поход к дыму вдвоём и одному, ужин и поход Маркуса в четвёртую ночь, культист пятой ночи, выбор внешности, игра без Scratch Creek. Живые плейтесты пока — на одном компьютере.

Ещё **не** проверено: игра через интернет между двумя ПК, живой микрофон, полное прохождение людьми, UPnP на реальном роутере, подключение через Steam.

**Известные ограничения:** синхронизируются только барменша и рейнджер, остальные NPC у каждого свои (ради личных скримеров); посадка в машину и выход без анимации; картинка экрана компьютера у партнёра небольшая и обновляется дважды в секунду.

### Сборка из исходников

Нужны .NET SDK (6+), установленная игра и распакованный [BepInEx 5.4.23.5 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). Команды — в английском разделе выше. Сборка ссылается на библиотеки игры из вашей установки; в репозитории их нет.

### Лицензия и благодарности

- Исходный код мода: [MIT](LICENSE).
- BepInEx (LGPL-2.1), HarmonyX, MonoMod, Mono.Cecil (MIT), Concentus (BSD-3), AssetsTools.NET и `classdata.tpk` из UABEA (MIT), библиотеки .NET (MIT). Подробно: [licenses/THIRD-PARTY.txt](licenses/THIRD-PARTY.txt).
- Fears to Fathom, Ironbark Lookout и Scratch Creek принадлежат их владельцам.
- Сделано с помощью ИИ-ассистента (Claude от Anthropic) под руководством автора и по его плейтестам.
