# SDK_API_LEDGER — ECAD-MCAD Bridge

Реестр API. Единственный источник истины о том, какие сигнатуры можно считать подтверждёнными.

## 0. Правила ведения

1. В раздел **Подтверждено** попадает только сигнатура, у которой есть доказательство:
   исходный код Altium SDK 4.0.1, метаданные сборки или успешный вызов в CAD-хосте.
2. Всё, что не имеет доказательства, находится в разделе **Требует проверки**.
3. Нельзя переносить сигнатуру из «Требует проверки» в «Подтверждено» без записи доказательства.
4. Для каждой подтверждённой сигнатуры указывается, где она используется.
5. При обновлении SDK реестр пересматривается; старые записи не удаляются, а помечаются
   `deprecated` с датой и причиной.
6. Reflection, `dynamic` и вызовы по строковому имени не являются подтверждённым API.
   Они фиксируются отдельно как временный обход.

## 1. Уровни и версии

| Уровень | Значение | Доказательство |
|---|---|---|
| API contract | Altium SDK 4.0.1 | `Altium Developer\SDK4.0\Version.txt`: `Altium Designer SDK Version 4.0.1`; `SDK.ini`: `Version=4.0.1 (AD 18.0+)` |
| SDK kit assembly | `Altium.SDK` 1.0.6542.30251, .NET Framework 4.0 | Метаданные `SDK4.0\CSharp\Altium.SDK.dll` |
| AD26 runtime assembly | `Altium.SDK` 1.0.7383.26074, .NET 8 | Метаданные `Altium.SDK.dll` из предоставленного InteractiveBom |
| AD26 runtime interfaces | `Altium.SDK.Interfaces` 1.0.0.0, .NET 8 | Метаданные `Altium.SDK.Interfaces.dll` из предоставленного InteractiveBom |
| Референсный Add-in | `InteractiveBomPlug` 1.0.0.0, `net8.0-windows` | `TargetFrameworkAttribute` и decompile |

Вывод: **4.0.1 — это версия API-контракта**, а не версия конкретной сборки, которую грузит AD26.
Совместимость AD26 + SDK 4.0.1 + .NET SDK 10 с `net10.0-windows` подтверждена оператором (C-65).
InteractiveBom под `net8.0-windows` — только референс, не целевой TFM.
Выбор reference assemblies для Altium `net10.0-windows` остаётся открытым вопросом V-02.

## 2. Подтверждено

### 2.1 Точка входа и регистрация модуля

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-01 | `object CSharpPlugin.PluginFactory.InvokePluginFactory(DXP.IClient client)` | Decompile `InteractiveBomPlug.dll`; SDK 4.0.1 `Examples\Server Template\Source Code\Main.cs` | Точка входа Add-in |
| C-02 | `[ComVisible(true)]`, `[ClassInterface(ClassInterfaceType.None)]` на классе фабрики | Decompile `InteractiveBomPlug.dll`; Server Template | COM-видимость фабрики |
| C-03 | `public DXP.ServerModule(DXP.IClient client, string moduleName)` | `DXP.Classes.cs`, класс `ServerModule` | Базовый конструктор модуля |
| C-04 | `protected abstract DXP.IServerDocument NewDocumentInstance(string kind, string fileName)` | `DXP.Classes.cs` | Обязательная реализация; для add-in вернуть `null` |
| C-05 | `protected virtual void InitializeCommands()` | `DXP.Classes.cs` | Регистрация команд |
| C-06 | `DXP.ICommandLauncher DXP.ServerModule.CommandLauncher { get; }` | `DXP.Classes.cs` | Доступ к launcher |
| C-07 | `void DXP.CommandLauncher.RegisterCommand(string commandId, DXP.CommandProc commandProc, DXP.GetStateProc getStateProc = null)` | `DXP.Classes.cs` | Регистрация команды |
| C-08 | `delegate void DXP.CommandProc(DXP.IServerDocumentView view, ref string parameters)` | `DXP.Classes.cs` | Тело команды |
| C-09 | `delegate void DXP.GetStateProc(DXP.IServerDocumentView view, ref string parameters, ref bool enabled, ref bool @checked, ref bool visible, ref string caption, ref string imageFile)` | `DXP.Classes.cs` | Состояние команды (опционально) |

### 2.2 Клиент, workspace и серверы

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-10 | `bool DXP.IClient.StartServer(string moduleName)` | `DXP.Interfaces.cs` | Запуск PCB-сервера |
| C-11 | `static DXP.IServerModule DXP.IClientHelper.GetServerModuleByName(this DXP.IClient client, string moduleName)` | `DXP.Interfaces.cs` (extension method) | Получение PCB-сервера |
| C-12 | `static DXP.IProductInfo DXP.IClientHelper.ProductInfo(this DXP.IClient client)` | `DXP.Interfaces.cs` | Проверка режима UI |
| C-13 | `bool DXP.IProductInfo.SupportsUIFeature(string feature, bool defaultValue)` | `DXP.Interfaces.cs` | `NoGUI` и подобные режимы |
| C-14 | `string DXP.IClient.GetDefaultExtensionForDocumentKind(string documentKind)` | `DXP.Interfaces.cs` | Расширение документа |
| C-15 | `DXP.IClient DXP.GlobalVars.Client { get; set; }`; `DXP.IDXPWorkSpace DXP.GlobalVars.DXPWorkSpace { get; }` | `DXP.Classes.cs` | Доступ к клиенту/workspace |
| C-16 | `EDP.IWorkspace` (совместим с `IDXPWorkSpace`); `object EDP.IWorkspace.Internal_DM_FocusedDocument()` | `EDP.Interfaces.cs` | Получение активного документа |
| C-17 | `DXP.IServerDocumentView` содержит только `Internal_GetOwnerDocument()`, `PerformAutoZoom()`, `UpdateStatusBar()` | `DXP.Interfaces.cs` | В API нет `GetState`; см. V-04 |
| C-18 | `object PCB.IPCB_ServerInterface.Internal_GetCurrentPCBBoard()` | `EDP.PCB.Interfaces.cs` | Получение текущей платы; см. V-03 |
| C-19 | `string PCB.IPCB_Board.GetState_FileName()` | `EDP.PCB.Interfaces.cs` | Имя файла платы |

### 2.3 Итераторы и объекты платы

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-20 | `static PCB.IPCB_BoardIterator PCB.IPCB_BoardHelper.BoardIterator_Create(this PCB.IPCB_Board board)` | `EDP.PCB.Interfaces.cs`; используется в InteractiveBom | Обход платы |
| C-21 | `static void PCB.IPCB_BoardHelper.BoardIterator_Destroy(this PCB.IPCB_Board board, ref PCB.IPCB_BoardIterator iterator)` | `EDP.PCB.Interfaces.cs` | Освобождение итератора |
| C-22 | `static void PCB.IPCB_BoardIteratorHelper.AddFilter_Method(this PCB.IPCB_BoardIterator iterator, PCB.TIterationMethod method)` | `EDP.PCB.Interfaces.cs` | Фильтр итерации |
| C-23 | `PCB.TIterationMethod.eProcessAll` | `EDP.PCB.Types.cs` | Значение фильтра |
| C-24 | `void PCB.IPCB_AbstractIterator.SetState_FilterAll()` | `EDP.PCB.Interfaces.cs` | Сброс фильтров |
| C-25 | `static void PCB.IPCB_AbstractIteratorHelper.AddFilter_ObjectSet(this PCB.IPCB_AbstractIterator iterator, PCB.TObjectSet set)` | `EDP.PCB.Interfaces.cs` | Фильтр по типам объектов |
| C-26 | `static PCB.IPCB_Primitive PCB.IPCB_AbstractIteratorHelper.FirstPCBObject(this PCB.IPCB_AbstractIterator iterator)` | `EDP.PCB.Interfaces.cs` | Первый объект |
| C-27 | `static PCB.IPCB_Primitive PCB.IPCB_AbstractIteratorHelper.NextPCBObject(this PCB.IPCB_AbstractIterator iterator)` | `EDP.PCB.Interfaces.cs` | Следующий объект |
| C-28 | `PCB.TObjectSet : HashSet<PCB.TObjectId>`; конструктор `TObjectSet()` и `Add(TObjectId)` | `EDP.PCB.Types.cs` | Набор типов объектов |
| C-29 | `PCB.TV6_LayerSet : HashSet<PCB.TV6_Layer>`; конструктор `TV6_LayerSet()` и `Add(TV6_Layer)` | `EDP.PCB.Types.cs` | Набор слоёв |
| C-30 | `static PCB.TV6_Layer PCB.IPCB_PrimitiveHelper.GetState_Layer(this PCB.IPCB_Primitive primitive)` | `EDP.PCB.Interfaces.cs` | Слой примитива |

### 2.4 Компоненты

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-31 | `static PCB.IPCB_Text PCB.IPCB_ComponentHelper.GetState_Name(this PCB.IPCB_Component component)` | `EDP.PCB.Interfaces.cs`; используется в InteractiveBom | Designator |
| C-32 | `static PCB.IPCB_Text PCB.IPCB_ComponentHelper.GetState_Comment(this PCB.IPCB_Component component)` | `EDP.PCB.Interfaces.cs`; используется в InteractiveBom | Comment |
| C-33 | `string PCB.IPCB_Component.GetState_Pattern()` | `EDP.PCB.Interfaces.cs` | Footprint |
| C-34 | `double PCB.IPCB_Component.GetState_Rotation()` | `EDP.PCB.Interfaces.cs` | Поворот |
| C-35 | `int PCB.IPCB_Component.GetState_Height()` | `EDP.PCB.Interfaces.cs` | Высота |
| C-36 | `string PCB.IPCB_Component.GetState_SourceUniqueId()` | `EDP.PCB.Interfaces.cs` | Стабильный ID, см. V-07 |
| C-37 | `bool PCB.IPCB_Component.GetState_FlippedOnLayer()` | `EDP.PCB.Interfaces.cs` | Сторона монтажа |

### 2.5 Примитивы

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-38 | `int PCB.IPCB_Pad.GetState_XLocation()` / `YLocation()` / `HoleSize()` / `TopXSize()` / `TopYSize()` / `BotXSize()` / `BotYSize()` / `HoleWidth()` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Пады |
| C-39 | `double PCB.IPCB_Pad.GetState_Rotation()` | `EDP.PCB.Interfaces.cs` | Поворот пада |
| C-40 | `static PCB.TShape PCB.IPCB_PadHelper.GetState_TopShape(this PCB.IPCB_Pad pad)` | `EDP.PCB.Interfaces.cs` | Форма пада |
| C-41 | `static PCB.TExtendedHoleType PCB.IPCB_PadHelper.GetState_HoleType(this PCB.IPCB_Pad pad)` | `EDP.PCB.Interfaces.cs` | Тип отверстия |
| C-42 | `int PCB.IPCB_Track.GetState_X1()` / `Y1()` / `X2()` / `Y2()` / `Width()` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Проводники |
| C-43 | `int PCB.IPCB_Via.GetState_XLocation()` / `YLocation()` / `HoleSize()` / `Size()` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Переходы |
| C-44 | `static PCB.IPCB_LayerObject PCB.IPCB_ViaHelper.GetState_StartLayer(this PCB.IPCB_Via via)` / `GetState_StopLayer()` | `EDP.PCB.Interfaces.cs` | Слои перехода |
| C-45 | `static PCB.TV6_Layer PCB.IPCB_LayerObjectHelper.V6_LayerID(this PCB.IPCB_LayerObject layerObject)` | `EDP.PCB.Interfaces.cs` | ID слоя |
| C-46 | `string PCB.IPCB_Text.GetState_Text()`; `int GetState_Width()`; `int GetState_Size()`; `bool GetState_Bold()`; `bool GetState_Italic()`; `static PCB.TTextKind PCB.IPCB_TextHelper.GetState_TextKind(this PCB.IPCB_Text text)` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Тексты |
| C-47 | `int PCB.IPCB_Fill.GetState_LocationX()` / `LocationY()` / `Length()` / `Width()` | `EDP.PCB.Interfaces.cs` | Заливки |
| C-48 | `int PCB.IPCB_Arc.GetState_CenterX()` / `CenterY()` / `Radius()` / `StartAngle()` / `EndAngle()` / `LineWidth()` | `EDP.PCB.Interfaces.cs` | Дуги |
| C-49 | `static PCB.IPCB_Contour PCB.IPCB_RegionHelper.GetMainContour(this PCB.IPCB_Region region)`; `int PCB.IPCB_Region.GetHoleCount()`; `static PCB.IPCB_Contour PCB.IPCB_RegionHelper.GetHole(this PCB.IPCB_Region region, int index)` | `EDP.PCB.Interfaces.cs` | Регионы |
| C-50 | `static PCB.IPCB_Net PCB.IPCB_PrimitiveHelper.GetState_Net(this PCB.IPCB_Primitive primitive)`; `string PCB.IPCB_Net.GetState_Name()` | `EDP.PCB.Interfaces.cs` | Цепи |

### 2.6 Контур платы

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-51 | `static PCB.IPCB_BoardOutline PCB.IPCB_BoardHelper.GetState_BoardOutline(this PCB.IPCB_Board board)` | `EDP.PCB.Interfaces.cs` | Контур платы |
| C-52 | `static PCB.IPCB_GeometricPolygon PCB.IPCB_BoardOutlineHelper.BoardOutline_GeometricPolygon(this PCB.IPCB_BoardOutline outline)` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Геометрический контур |
| C-53 | `int PCB.IPCB_GeometricPolygon.GetState_Count()`; `bool PCB.IPCB_GeometricPolygon.GetState_IsHole(int index)` | `EDP.PCB.Interfaces.cs` | Контуры и отверстия |
| C-54 | `static PCB.IPCB_Contour PCB.IPCB_GeometricPolygonHelper.GetState_Contour(this PCB.IPCB_GeometricPolygon polygon, int index)` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Контур по индексу |
| C-55 | `int PCB.IPCB_Contour.GetState_Count()`; `int PCB.IPCB_Contour.GetState_PointX(int index)`; `int PCB.IPCB_Contour.GetState_PointY(int index)` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Точки контура |
| C-56 | `static PCB.PolySegment PCB.IPCB_PolygonHelper.GetState_Segments(this PCB.IPCB_Polygon polygon, int index)` | `EDP.PCB.Interfaces.cs` | Сегменты полигона |

### 2.7 Единицы и геометрия

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-57 | `static double EDP.Utils.CoordToMMs(int coord)` | `EDP.Util.cs`; InteractiveBom | IU → мм; IU сохраняется в `xIu/yIu` |
| C-58 | `static int EDP.Utils.MMsToCoord(double mm)` | `EDP.Util.cs`; InteractiveBom | мм → IU; результат записывается в `xIu/yIu` |
| C-59 | `static int EDP.Utils.MilsToCoord(double mils)` | `EDP.Util.cs` | Mils → Coord |
| C-60 | `PCB.ICoordRect.GetLeft()` / `GetBottom()` / `GetRight()` / `GetTop()` | `EDP.PCB.Interfaces.cs`; InteractiveBom | Bounding box |

### 2.8 Контракт хоста (не API, но проверено)

| ID | Контракт | Доказательство |
|---|---|---|
| C-61 | `.Ins` начинается с `ClientInsFile 1.0`; секция `Server` содержит `EditorName`, `EditorExePath`, `EditorDescription`, `Version`, `Date`, `HelpAboutInfo`, `Copyright`; команды объявляются секцией `Command` | `InteractiveBomPlug.Ins`; Server Template `.ins` |
| C-62 | `.Ins` может содержать `PL`-секции и `Insertion` для меню | `InteractiveBomPlug.Ins` |
| C-63 | `.rcs` связывает PLID с командой, иконкой, caption и точкой вставки в меню | `InteractiveBomPlug.rcs` |
| C-64 | Регистрация расширения выполняется элементом `Item` в `ExtensionsRegistry.xml` с `HRID`, `Guid`, `Path`, `Status`, `Title`, `Version`, `PlatformVersions` | `ExtensionsRegistry.xml` на стенде; инструкция предоставленного проекта |
| C-65 | Подтверждена совместимость Altium Designer 26 + Altium SDK 4.0.1 + .NET SDK 10 с `net10.0-windows` для Altium Add-in | Подтверждение оператора в ревью `BRIDGE-BOOTSTRAP-002`; целевой TFM Altium, не InteractiveBom `net8.0-windows` |

## 3. Требует проверки

| ID | Вопрос | Почему не подтверждено | Как проверить |
|---|---|---|---|
| V-02 | Какие reference assemblies использовать: kit 4.0.1 (.NET Framework 4.0) или runtime-сборки AD26 (.NET 8, 1.0.7383.26074)? | Версии и TFM различаются; возможны несовместимости | Сравнить публичный API сборок, собрать оба варианта, проверить загрузку |
| V-03 | Работает ли прямой типизированный вызов `IPCB_ServerInterface.Internal_GetCurrentPCBBoard()` в AD26? | Референсный Add-in вызывает `GetCurrentPCBBoard` через reflection/dynamic | Smoke-тест прямого вызова; при отказе — зафиксировать обход |
| V-04 | Как официально получить активный документ/вид без reflection? | В `IServerDocumentView` нет `GetState`; референс использует reflection | Исследовать `IWorkspace.Internal_DM_FocusedDocument` и `IClient` API; проверить в хосте |
| V-05 | Как определить несохранённые изменения и хеш `.PcbDoc`? | `GetModified`, `GetFileName` объявлены, но поведение не проверено | Smoke-тест на новом/изменённом/сохранённом документе |
| V-06 | Как извлекать 3D-модели и высоты для MCAD? | `SaveModelToFile`, `DefaultPCB3DModel` объявлены, поведение и формат не проверены | Отдельная задача MCAD-фазы; не использовать до проверки |
| V-07 | Стабилен ли `SourceUniqueId` между сессиями и ECO? | Не проверено; возможны пустые/меняющиеся значения | Экспорт одного проекта до/после ECO, сравнение |
| V-08 | Как получить толщину платы и стек слоёв? | Интерфейсы слоёв объявлены, но нужный путь не подтверждён | Исследовать `LayerStack`/`BoardLayerSetManager` |
| V-09 | Как корректно перечислять механические слои и их назначение? | `TV6_Layer` известен, семантика механических слоёв не проверена | Smoke-тест на тестовой плате |
| V-10 | Семантика `GetState_Height()` и bounding box компонента | Не проверено, в каких единицах и с учётом чего считается | Сравнить с UI Altium на эталонной плате |
| V-11 | Автоматизация установки расширения и правки `ExtensionsRegistry.xml` | Известен только ручной путь | Отдельная задача; учесть права администратора и блокировку файла |
| V-12 | Лицензирование и допустимость поставки SDK-сборок Altium вместе с Add-in | Требуется проверка лицензии; в репозиторий DLL не коммитятся | Юридическая/техническая проверка, решение в `DECISIONS.md` |
| V-13 | Точный build AD26 и версия runtime-сборок | На стенде разные версии assembly; build не зафиксирован | Записать `Help → About`, версии DLL, дату проверки |
| V-14 | API SOLIDWORKS 2026 (interop, версия, загрузка Add-in) | Ничего не проверено; в проекте нет кода | Отдельная задача MCAD-фазы |
| V-15 | Поддержка WPF/WinForms внутри AD26 и тема оформления | Референс использует WPF и DevExpress, но это не согласованный стек | Минимальный UI-smoke-тест |
| V-16 | Требования к потоку и COM-апартаменту для вызовов PCB API | Не проверено; возможны UI-thread-only ограничения | Smoke-тест с фоновым потоком и без него |
| V-17 | Поведение `TIterationMethod.eProcessAll` на всех типах объектов | Значение известно, полный набор объектов не проверен | Сравнить счётчики с UI/отчётом Altium |

## 4. Запрещено

| Запрет | Комментарий |
|---|---|
| DelphiScript | Не используется ни в каком виде |
| Legacy API Altium | Только интерфейсы Altium SDK 4.0.1; старые Delphi/COM-обвязки запрещены |
| Прямой парсинг `.PcbDoc` | Только через `PCB.IPCB_*` |
| Постоянное использование reflection | Допустимо только как временный обход с записью в V-раздел и планом замены |
| Вызовы недокументированных DispId | Запрещены |
| Запись в плату на этапе `BRIDGE-000001` | Запрещена |
| API SOLIDWORKS в категории «подтверждено» | Пока нет ни одной проверенной сигнатуры |

## 5. SOLIDWORKS 2026

Раздел намеренно пуст в категории «Подтверждено».

Планируемые к проверке вопросы (без фиксации сигнатур):

- какой interop использовать для SOLIDWORKS 2026;
- как загружается Add-in и как регистрируется;
- как получить сборку/деталь и дерево компонентов;
- как создавать/обновлять геометрию без разрушения пользовательской модели;
- как сопоставлять MCAD-компоненты с ECAD-идентификаторами.

Все пункты ведутся как V-14 и не влияют на текущую архитектуру.

## 6. Журнал изменений

| Дата | Изменение | Автор |
|---|---|---|
| 2026-11-23 | Создан реестр; подтверждены сигнатуры по SDK 4.0.1 и метаданным InteractiveBom | Bootstrap |
| 2026-11-24 | BRIDGE-BOOTSTRAP-002: добавлен C-65, закрыт V-01, уточнены C-57/C-58 для пары IU/мм | Bootstrap |
