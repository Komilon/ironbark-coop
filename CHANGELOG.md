# Changelog / Изменения

Both players must use the same mod version. / У обоих игроков должна быть одна версия мода.

## 0.6.0 — playtest fixes: the next night, the morning, flashlights, spray / исправления по плейтесту

**English**
- After the campfire level the tower could open on night 1 for one player (back to the generator, the key, Connor...): the next night was set only in the game of whoever put out the fire. The mod now remembers it and sends it to the partner, so both open the right night (also with two copies on one PC).
- Night 2: the dreaming Marcus no longer stands up in the tower for a moment when Tessa goes back to sleep; he wakes once the screen is black.
- Morning after night 2: Marcus has his coffee at the rail (seen standing, not sitting and turned away); Tessa wakes up still in bed and gets up with Space.
- A thrown flashlight is followed until it lies still (and if it is knocked later), so it lies the same way for both.
- Bear spray: the cloud now leaves the partner's can in the direction it points.
- The look picker frees its preview texture and material when closed.

**Русский**
- После уровня с костром вышка у одного игрока могла открыться на первой ночи (снова генератор, ключ, Коннор…): следующую ночь игра записывала только тому, кто тушил огонь. Мод теперь запоминает её и передаёт партнёру — оба попадают в нужную ночь (и на одном ПК тоже).
- Вторая ночь: спящий Маркус больше не встаёт на секунду в вышке, когда Тесса ложится обратно, — просыпается, когда экран уже чёрный.
- Утро после второй ночи: Маркус пьёт кофе у перил (виден стоящим, а не сидящим спиной), Тесса просыпается в кровати и встаёт Пробелом.
- Брошенный фонарик отслеживается, пока не перестанет катиться (и если его потом задели), — у обоих он лежит одинаково.
- Спрей: облако идёт из баллончика партнёра туда, куда он направлен.
- Окно выбора внешности освобождает память при закрытии.

## 0.5.0 — Scratch Creek optional, look picker / Scratch Creek не обязательна, выбор внешности

Not compatible with 0.4.0 in the same session (update both). / С 0.4.0 в одной игре не работает — обновитесь оба.

**English**
- Scratch Creek is no longer needed: Ironbark's own people (the ranger, the diner waitress, the man in a suit) are read from your Ironbark install the first time the game starts (well under a second) and used as partner bodies, with every motion (crouching and sleeping are made from Ironbark's standing and walking clips).
- Look picker in the Multiplayer menu: a 3D view you turn with the mouse, every pose (standing, walking, running, crouching, sitting, asleep), Scratch Creek's Marcus and Tessa when you have it. The pick is sent to your partner and can be changed while connected; a partner without your pick sees a similar Ironbark character.
- Role (who leads the story) and look are separate settings.
- With Scratch Creek, Ironbark's people move with Scratch Creek's animations.
- New config: `Character / Look`; `Character / ScratchCreekPath = none` ignores Scratch Creek.

**Русский**
- Scratch Creek больше не нужна: люди из самой Ironbark (смотритель, официантка из закусочной, мужчина в костюме) при первом запуске читаются из вашей установленной Ironbark (меньше секунды) и становятся телом партнёра — со всеми движениями (присед и сон собраны из стойки и ходьбы Ironbark).
- Выбор внешности в меню мультиплеера: 3D-просмотр, модель крутится мышью, все позы (стоит, идёт, бежит, присел, сидит, спит), Маркус и Тесса из Scratch Creek, если она есть. Выбор передаётся партнёру и меняется и после подключения; у кого нет вашего персонажа, увидит похожего из Ironbark.
- Роль (кто ведёт сюжет) и внешность — отдельные настройки.
- Со Scratch Creek люди из Ironbark двигаются с анимациями Scratch Creek.
- Новые настройки: `Character / Look`; `Character / ScratchCreekPath = none` — не использовать Scratch Creek.

## 0.4.0 — first public release / первый публичный релиз

Playtest fixes from five sessions and a full pass through nights 1–3. Network protocol 5 (not compatible with 0.3.0).
Исправления по пяти плейтестам и полный проход ночей 1–3. Сетевой протокол 5 (с 0.3.0 несовместим).

**English**
- Sleep together, as in Scratch Creek: whoever lies down first waits on their half of the bed (Space gets up); the night ends when both are in bed. Each sleeps on their own half; the partner is shown in Scratch Creek's sleeping pose.
- Night 2 reworked: Tessa wakes up for the night (bed radio, washroom); Marcus dreams — a dark forest walk with a flashlight, a heavy blur that clears over the first steps, a cold glow, grain, the dream's own sound; he hears Tessa far away with reverb and is left out of the night's scripts. Items in his hands stay out of the dream.
- Diner: fixed sides (Marcus on the served bench, Tessa across), a separate tray for each, Marcus waits until both are served, each eats from their own tray; the waitress's tray arm is mirrored.
- RV: passenger eye at the real passenger seat; a bag in the RV no longer stalls it and rides along; the ride's shake is felt by a partner in the cab (not outside).
- Flashlights: the partner's flashlight is always visible; a picked-up partner flashlight has the right size in the hand.
- Radio talks: the listener sees the other side's lines; the phone (Esc) hides the talk panel; a dreaming player hears no talks; duplicate talks are merged.
- Tower: one computer for both (booted once, open windows shared); the desk chair can't be taken twice through a monitor click; firewood can be taken and put in the stove by either player and is visible in hand; morning coffee — each stands at their own place at the rail.
- Bear spray cloud comes out of the partner's can; the gas can's stream is visible.
- Two copies on one PC: a background window no longer reads mouse buttons/movement (one player drinking coffee made both drink).
- Co-op chores only on calm nights 4 and 6; the generator stall chore is temporarily off.

**Русский**
- Сон вдвоём, как в Scratch Creek: кто лёг первым, ждёт на своей половине кровати (Пробел — встать); ночь кончается, когда легли оба. Партнёр лежит в позе сна из Scratch Creek.
- Вторая ночь переделана: Тесса просыпается (рация в кровати, туалет); Маркус видит сон — тёмный лес с фонариком, сильное размытие, проясняющееся на первых шагах, холодное свечение, зерно, звук сна; Тессу он слышит издалека с эхом и в скриптах ночи не участвует. Предметы в руках в сон не попадают.
- Закусочная: места закреплены (Маркус — где подают еду, Тесса — напротив), каждому свой поднос, Маркус ждёт, пока принесут обоим, каждый ест со своего; рука официантки с подносом синхронизирована.
- Фургон: правильная точка обзора пассажира; сумка в фургоне больше не тормозит его и едет вместе с ним; тряску чувствует и партнёр в салоне (снаружи — нет).
- Фонарики: фонарик партнёра всегда виден; подобранный фонарик партнёра нормального размера в руке.
- Рация: слушающий видит реплики собеседника; телефон (Esc) прячет панель разговора; спящий не слышит разговоров; двойные разговоры объединяются.
- Вышка: один компьютер на двоих (включается один раз, окна общие); занятый стул нельзя занять кликом по монитору; дрова может взять и положить в печь любой, они видны в руке; утренний кофе — каждый на своём месте у перил.
- Облако спрея идёт из баллончика партнёра; струя из канистры видна.
- Две копии на одном ПК: фоновое окно больше не читает мышь (один пил кофе — пили оба).
- Кооп-обязанности только в спокойные ночи 4 и 6; поломка генератора временно отключена.

## 0.3.0 (not published / не публиковался)

Multiplayer in the game's main menu, join code and experimental UPnP, Scratch Creek survivors (Marcus/Tessa) with their animations, shared RV rides with a passenger seat, visible partner flashlight and speaking icon, voice and poses over UDP.
Мультиплеер в главном меню, код подключения и экспериментальный UPnP, Маркус и Тесса из Scratch Creek с анимациями, совместные поездки с пассажирским сиденьем, видимый фонарик партнёра и иконка речи, голос и движение по UDP.

## 0.2.0 (not published / не публиковался)

First playable build: shared story, personal inventory and scares, solo respawn, co-op chores, 3D proximity voice, all campaign scenes, continue and reconnect.
Первая играбельная сборка: общий сюжет, личные инвентарь и скримеры, одиночное возрождение, кооп-обязанности, 3D-голос, все сцены кампании, продолжение и переподключение.
