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
| AD26 runtime assembly (установленный AD) | `Altium.SDK` 1.0.7383.26074; встречены варианты .NET 8 (`ActiveRoute`, `Draftsman`) и .NET 10 (`Tasklist`, `Mixed Simulation`) | Метаданные DLL из `C:\ProgramData\Altium\Altium Designer {…}\Extensions\...\Altium.SDK.dll`; **не из `InteractiveBom.zip`** |
| AD26 runtime interfaces (установленный AD) | `Altium.SDK.Interfaces` 1.0.0.0; варианты .NET 8 и .NET 10 | Метаданные DLL из `...\Extensions\...\Altium.SDK.Interfaces.dll`; **не из `InteractiveBom.zip`** |
| Копия runtime-сборок в бинарной поставке | `Altium.SDK` 1.0.7383.26074, .NET 8; `Altium.SDK.Interfaces` 1.0.0.0, .NET 8 | Метаданные `InteractiveHtmlBom_ADPlug-master/publish_out/Altium.SDK.dll` и `Altium.SDK.Interfaces.dll`; совпадает с .NET 8-вариантом установленных extension |
| Источник runtime-сборок в исходном проекте | `$(AltiumSystemDir)\Altium.SDK.dll` и `$(AltiumSystemDir)\Altium.SDK.Interfaces.dll`, `Private=false` | Общие MSBuild-файлы шаблона Altium SDK (`Directory.Build.targets`, строка 35 и ниже): сборки предоставляются запущенным AD и не копируются в выход проекта |
| Референсный Add-in (исходники) | `InteractiveBom.zip`: `InteractiveBom.sln` с проектами shim / Impl / Panels / Tests; указанная Release-сборка `bin/Release/net10.0-windows` | Состав и TFM по заданию `BRIDGE-BOOTSTRAP-004`; COM-контракт и hot-reload-схема сверены с shim-шаблоном Altium SDK (`framework/shim/PluginServerModule.cs`, `Implementation.cs`, `Directory.Build.props`) |
| Отдельная бинарная поставка | `InteractiveBomPlug` 1.0.0.0, DLL под `net8.0-windows` | `TargetFrameworkAttribute` (`.NETCoreApp,Version=v8.0`, `net8.0-windows`) в локальной копии `publish_out/InteractiveBomPlug.dll`; другой источник, не Release-сборка архива |

Вывод: **4.0.1 — это версия API-контракта**, а не версия конкретной сборки, которую грузит AD26.
Архив `InteractiveBom.zip` не содержит `Altium.SDK.dll` и `Altium.SDK.Interfaces.dll`; в исходном
проекте они подключаются из `AltiumSystemDir` с `Private=false`. Фактический источник метаданных —
установленные extension-сборки AD26 в `C:\ProgramData\Altium\Altium Designer {…}\Extensions`
(номер сборки 1.0.7383.26074 / 1.0.0.0, встречаются .NET 8 и .NET 10 варианты). Копия в
`publish_out` — отдельная бинарная поставка, а не содержимое архива. Конкретный набор сборок,
который грузит целевой стенд, остаётся открытым (V-13).
Совместимость AD26 + SDK 4.0.1 + .NET SDK 10 с `net10.0-windows` подтверждена оператором (C-65).
TFM InteractiveBom зависит от версии Altium: `Directory.Build.props` читает `X2.runtimeconfig.json`
(AD 26.1 → `net8.0`, AD 26.12 → `net10.0`). TFM Bridge зафиксирован как `net10.0-windows`.
Для `BRIDGE-000001` в качестве reference assemblies использованы сборки установленного AD26
(`$(AltiumSdkDir)\Altium.SDK.dll` и `Altium.SDK.Interfaces.dll`, `Private=false`), а не kit 4.0.1
(.NET Framework 4.0). Решение сборки и загрузки в хосте зафиксировано в V-02; API-контракт 4.0.1
остаётся источником сигнатур.

## 2. Подтверждено

### 2.1 Точка входа и регистрация модуля

| ID | Сигнатура | Доказательство | Использование |
|---|---|---|---|
| C-01 | `object CSharpPlugin.PluginFactory.InvokePluginFactory(DXP.IClient client)` | Задание `BRIDGE-BOOTSTRAP-004` по исходному shim `InteractiveBom.zip`; shim-шаблон Altium SDK `framework/shim/PluginServerModule.cs`; SDK 4.0.1 `Examples\Server Template\Source Code\Main.cs` | Точка входа Add-in |
| C-02 | Assembly-level `[ComVisible(true)]`; `PluginFactory : IPluginFactory` с `[ClassInterface(ClassInterfaceType.AutoDispatch)]`; `PluginServerModule` тоже `AutoDispatch` | shim-шаблон Altium SDK `framework/shim/PluginServerModule.cs`; `IPluginFactory` из SDK 4.0.1; задание `BRIDGE-BOOTSTRAP-004` по `InteractiveBom.zip` | COM-видимость фабрики и модуля по образцу исходного проекта |
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
| C-18 | `static PCB.IPCB_Board PCB.IPCB_ServerInterfaceHelper.GetCurrentPCBBoard(this PCB.IPCB_ServerInterface serverInterface)` | Метаданные `Altium.SDK.Interfaces.dll` 1.0.0.0 AD26; успешный типизированный вызов в smoke-тесте AD26 26.12.2.9 (`SNAPSHOT_OK`, `components=28`) | Получение текущей платы без reflection; R-03 |
| C-19 | `string PCB.IPCB_Board.GetState_FileName()` | `EDP.PCB.Interfaces.cs`; smoke-тест вернул `board.PcbDoc` в JSON | Имя файла платы (только имя, без пути) |
| C-66 | `static PCB.IPCB_ServerInterface PCB.GlobalVars.PCBServer { get; }` | Метаданные `Altium.SDK.Interfaces.dll` 1.0.0.0 AD26 (`PCB.GlobalVars.get_PCBServer`); успешный вызов в smoke-тесте | Доступ к PCB-серверу |

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
| C-57 | `static double EDP.Utils.CoordToMMs(int coord)` | `EDP.Util.cs`; InteractiveBom | IU → мм; 1 IU = 0.00000254 mm; исходное IU сохраняется в `*Iu` для всех линейных величин |
| C-58 | `static int EDP.Utils.MMsToCoord(double mm)` | `EDP.Util.cs`; InteractiveBom | мм → IU; результат записывается в `*Iu`; тесты на контрольных значениях обязательны |
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

### 2.9 Altium BoardSnapshot adapter — BRIDGE-000003

Сигнатуры проверены по метаданным `Altium.SDK.Interfaces.dll` установленного AD26
(`ildasm`) и фактическим вызовом в AD26.12.2.9. Контракт SDK 4.0.1 остаётся источником
назначения API; для adapter использованы только типизированные вызовы, без reflection.

| ID | API | Назначение / ограничение |
|---|---|---|
| C-73 | `PCB.IPCB_BoardHelper.LayerIterator()` → `IPCB_LayerObjectIterator`; `First()`, `Next()`, `LayerObject()` | Перебор слоёв платы |
| C-74 | `IPCB_LayerObject.GetState_LayerName()`, `Internal_V6_LayerID()`, `IsInLayerStack()`, `GetState_UsedByPrims()`, `V7_LayerID()` | Имя, V6-ID, признак stackup и использования слоя |
| C-75 | `PCB.IPCB_BoardHelper.GetState_MasterStack()`; `IPCB_LayerStackBaseHelper.Get_ZTop/ZBottom(IPCB_LayerObject)` | Толщина платы по master stack; значения могут быть 0/недоступны |
| C-76 | `IPCB_Board.BoardIterator_Create/Destroy`; `AddFilter_ObjectSet(TObjectSet)`, `AddFilter_Method(TIterationMethod.eProcessAll)`, `FirstPCBObject/NextPCBObject` | Перебор `eComponentObject`, `eNetObject` |
| C-77 | `IPCB_Component.GetState_Name()`, `GetState_Comment()`, `GetState_Pattern()`, `GetState_SourceUniqueId()`, `GetState_UniqueId()`, `GetState_Layer()`, `GetState_FlippedOnLayer()`, `GetState_XLocation/YLocation()`, `GetState_Rotation()`, `GetState_Height()` | Компоненты, designator/comment/footprint, placement, сторона, высота |
| C-78 | `IPCB_Group.GroupIterator_Create/Destroy`; `AddFilter_ObjectSet`, `AddFilter_AllLayers`, `FirstPCBObject/NextPCBObject` | Перебор падов компонента |
| C-79 | `IPCB_Pad.GetState_Name/UniqueId/Layer/XLocation/YLocation/Rotation/HoleSize/Net`; `GetState_ShapeOnLayer`, `GetState_XSizeOnLayer/YSizeOnLayer`, `GetState_XStackSizeOnLayer/YStackSizeOnLayer` | Пады, слои, формы, размеры, net, отверстия (только чтение) |
| C-80 | `IPCB_Net.GetState_Name()`, `GetState_UniqueId()` | Nets |
| C-81 | `IPCB_BoardOutline.BoardOutline_GeometricPolygon()`; `IPCB_GeometricPolygon.GetState_Count/GetState_Contour/GetState_IsHole`; `IPCB_Contour.GetState_Count/PointX/PointY` | Контуры, роли outer/hole и геометрический fallback |
| C-82 | `IPCB_BoardOutline` как `IPCB_Polygon`: `GetState_PointCount()`, `GetState_Segments(int)`; `PolySegment.Kind/Vx/Vy/Cx/Cy/Radius/Angle1/Angle2` | Контур с аналитическими дугами (`ePolySegmentArc`) |
| C-83 | `TObjectId.eComponentObject/eNetObject/ePadObject/eTrackObject`; `TV6_Layer`; `TShape`; `TPolySegmentType` | Фильтры и преобразование layer/shape/segment |
| C-84 | `DXP.IServerDocumentView.GetOwnerDocument()`; `IServerDocument.GetKind()`; `IPCB_Board.GetState_FileName()` | Проверка активного PCB и имени документа; ранее C-19/C-67/C-68 |
| C-85 | `static PCB.TRegionKind PCB.IPCB_RegionHelper.GetState_Kind(this PCB.IPCB_Region region)`; `PCB.TRegionKind.eRegionKind_BoardCutout` / `eRegionKind_Copper` / `eRegionKind_Cutout` / `eRegionKind_NamedRegion` / `eRegionKind_Cavity` | Тип региона; enum проверен по `Altium.SDK.Interfaces.dll` AD26 |
| C-86 | `PCB.IPCB_Region.GetState_Name()`, `GetState_Area()` (`int64`), `GetState_UniqueId()`; `static PCB.IPCB_GeometricPolygon PCB.IPCB_RegionHelper.GetGeometricPolygon(this PCB.IPCB_Region)`; `GetMainContour`, `GetHoleCount`, `GetHole(int)` | Инвентаризация регионов и их контуров; только чтение |
| C-87 | `static PCB.IPCB_PolyRegShapesClipResult PCB.IPCB_BoardOutlineHelper.ClipWithPolyRegShapes(this PCB.IPCB_BoardOutline outline, bool useHolesFromViaPad, bool useEmbedded)` | Неразрушающее вычисление вырезов контура; `false,false` исключает pad/via holes |
| C-88 | `static PCB.IPCB_Shapes PCB.IPCB_PolyRegShapesClipResultHelper.GetCutouts/GetNewPrims/GetInoperablePrims(this PCB.IPCB_PolyRegShapesClipResult)`; `int PCB.IPCB_Shapes.Count()`; `static PCB.IPCB_RegionShape PCB.IPCB_ShapesHelper.Items(this PCB.IPCB_Shapes, int)`; `int PCB.IPCB_RegionShape.GetState_ShapeSegmentCount()`; `static PCB.PolySegment PCB.IPCB_RegionShapeHelper.GetState_ShapeSegment(this PCB.IPCB_RegionShape, int)` | Извлечение клипованных Board Cutout как линий/дуг |
| C-89 | `string DXP.IServerDocument.GetFileName()`; `bool DXP.IServerDocument.GetModified()`; `string DXP.IServerDocument.GetKind()`; `static DXP.IServerDocument DXP.IServerDocumentViewHelper.GetOwnerDocument(this DXP.IServerDocumentView)` | Сохранённый PCB: полный absolute path, `GetModified=false`; новый PCB: относительное `PCB1.PcbDoc`, путь не absolute; после изменения `GetModified=true`; `DoFileSave` → `true` и `GetModified=false` (AD26.12.2.9 smoke) | Источник metadata: путь/состояние перед извлечением и повторно после |
| C-90 | `TObjectId.eTrackObject`; `IPCB_Track.GetState_X1/Y1/X2/Y2/Width`; `IPCB_PrimitiveHelper.GetState_Layer/GetState_Net`; `IPCB_Primitive.GetState_UniqueId` | Tracks на MiniPC/Bluetooth/cutout smoke; 48 299 / 10 038 / 10 657 объектов; `GetState_UniqueId` вернул пустую строку, использован fallback по индексу | `Track` |
| C-91 | `TObjectId.eViaObject`; `IPCB_Via.GetState_XLocation/YLocation/Size/HoleSize`; `IPCB_ViaHelper.GetState_StartLayer/StopLayer`; `IPCB_PrimitiveHelper.GetState_Layer/GetState_Net`; `IPCB_Primitive.GetState_UniqueId` | Vias на всех трёх платах; 4 025 / 173 / 173; `GetState_UniqueId` пустой, fallback по индексу | `Via` |
| C-92 | `IPCB_Region.GetState_Kind/Name/Area/HoleCount/GetGeometricPolygon/GetMainContour/GetHole`; `IPCB_Primitive.GetState_Layer/GetState_Net/GetState_IsKeepout/GetState_UniqueId`; `TRegionKind` C-85 | Regions на всех трёх платах; 381 / 12 / 12 core regions, 1 / 3 / 5 Board Cutout; `GetState_UniqueId` пустой | `Region`; Board Cutout остаётся в outline |
| C-93 | `TObjectId.eTextObject`; `IPCB_Text.GetState_Text/Size/Width/Bold/Italic/IsDesignator/IsComment`; `IPCB_TextHelper.GetState_TextKind`; `IPCB_RectangularPrimitive.GetState_XLocation/YLocation/Rotation`; `IPCB_PrimitiveHelper.GetState_Layer/GetState_Component`; `IPCB_Primitive.GetState_UniqueId` | Texts на всех трёх платах; экспортировано 34 / 87 / 87; пропущено designator/comment 146 (2×73), 2 093 (2×1046 + 1 barcode); `GetState_UniqueId` пустой | `TextItem` |

Подтверждено smoke-тестом на `MiniPC.PcbDoc`: 31 слой, 1046 компонентов, 6004 пада,
837 сетей, 245 multi-layer PadStack с 16 shapes, 9669 форм; `rotations` включают
`135°`, стороны Top/Bottom представлены. На `Bluetooth_Sentinel.PcbDoc` подтверждены
2 аналитические дуги в контуре и 12 сегментов (10 линий + 2 дуги).

FIX-001 уточнения:

- `GeometricPolygon` является источником полного набора контуров и ролей `GetState_IsHole`;
  `IPCB_Polygon.GetState_Segments()` используется только как источник аналитических
  линий/дуг и может не покрывать все циклы (на `PC4-SODIMM` получены незамкнутые циклы).
  Adapter сопоставляет каждый geometric contour с аналитическим циклом; при отсутствии
  совпадения контур не теряется, а экспортируется line-fallback с предупреждением.
- `TShape`:
  - точные: `eRectangular`, `eCircleShape`, `eRounded` при равных размерах;
  - упрощённые (warning): `eOctagonal` (chamfered rectangle → Core Octagonal),
    `eRoundRectShape`/`eRoundedRectangular` (corner radius API не подтверждён),
    `eRounded` с разными размерами (oval → rounded rectangle radius=min/2);
  - неподдерживаемые (warning, не подменяются): `eRotatedRectShape`, `eArcShape`,
    `eTerminator`, `eCustomShape`, `eNoShape`.
- `TShape.eCustomShape` и pad holes не имеют полноценного представления в Core 1.0.0;
  adapter пишет предупреждение и не выдумывает геометрию.
- `LayerKind` Core не различает signal и internal plane: оба `Copper`.
- Локальные координаты падов, pin designator, stack order и наличие 3D-модели в Core
  отсутствуют; adapter их не подменяет.
- Внутренние вырезы контура: `GeometricPolygon.GetState_IsHole` на проверенных
  платах не дал hole-контуров, поэтому вырезы извлекаются из `IPCB_Region` с
  `eRegionKind_BoardCutout` и подтверждаются `ClipWithPolyRegShapes(false,false)`
  (C-85…C-88). Тип региона сам по себе недостаточен: `MiniPC.PcbDoc` содержит
  `eRegionKind_BoardCutout` («Layer Stack Region 1»), но клиппер возвращает 0
  вырезов; такие регионы остаются только диагностикой и не экспортируются.
  Реальный `board with cutout.PcbDoc` дал 2 выреза, включая дуги; V-22 закрыт.
  Pad/via holes исключены параметром `useHolesFromViaPad=false` и проверены
  платой с 6004 падами, где клиппер вернул 0 вырезов.
- `PolySegment.Angle1/Angle2` для `ePolySegmentArc` задают обход **против часовой
  стрелки**; `Angle2 < Angle1` означает переход через 0°. Это подтверждено на
  cutout-регионе: arc `270°→0°` является короткой четвертью, а не CW-дугой 270°.
  После исправления (FIX-005) geometric contour региона 0 совпадает с clip loop:
  area Δ 0.14%, centroid Δ 692.61 IU, max vertex Δ 3096.57 IU.
- FIX-005 дедупликация сравнивает **фактические `geometric.Points`** Hole-контура
  с clip loop; самосравнение clip loop устранено. Partial overlap не подавляет
  экспорт.
- FIX-005 Region matching: bounding box 10 IU — только broad phase; затем
  вершины Region-контура (3500 IU), площадь (5%) и centroid (1000 IU).
  Результат различает `Matched` / `Unmatched` / `Ambiguous`; связь хранится по
  паре `(RegionIndex, ContourIndex)`, повторное назначение контура запрещено.
  Неоднозначность не разрешается произвольным выбором.
- FIX-006: bounds аналитической дуги вычисляются по Start/End и кардинальным
  экстремумам, реально входящим в sweep (CW/CCW, переход 0°/360°, >180°); sampling
  не используется для точных bounds. Broad phase переведён в geometry-first:
  bounds остаются диагностикой/ранжированием и не отбрасывают Region. Dedup
  дуговых Hole использует отдельный approximation tolerance 3500 IU, двустороннее
  покрытие, площадь 5% и centroid 100 IU; exact tolerance остаётся 2 IU.
- FIX-007: для approximation-dedup centroid сравнивается по характерным точкам
  (вершины Hole, спроецированные на clip loop), а не по raw polygon centroid.
  Это устраняет ложный отказ при большой дуге и разной плотности полилинии;
  перенос независимого контура остаётся диагностируемым.

### 2.10 Доказательства, полученные в smoke-тесте BRIDGE-000001

| ID | Сигнатура / вызов | Доказательство | Использование |
|---|---|---|---|
| C-67 | `static DXP.IServerDocument DXP.IServerDocumentViewHelper.GetOwnerDocument(this DXP.IServerDocumentView view)` | Метаданные `Altium.SDK.Interfaces.dll` 1.0.0.0 AD26; успешный вызов в smoke-тесте | Активный документ без reflection (R-04) |
| C-68 | `string DXP.IServerDocument.GetKind()` | Успешный вызов в smoke-тесте: документ `PCB` прошёл проверку, иначе команда вернула бы ошибку | Проверка того, что активный документ — PCB |
| C-69 | `static void DXP.Utils.ShowInfo(string argText, string argCaption)` / `static void DXP.Utils.ShowWarning(string argText, string argCaption)` | Метаданные `Altium.SDK.Interfaces.dll` (`DXP.Utils`); в smoke-тесте появился `TMessageForm` с заголовком `EcadMcadBridge` и кнопкой `OK` | Показ пути к JSON и сообщений об ошибке |
| C-70 | Повторное подтверждение C-20…C-29, C-51…C-55, C-57 | Smoke-тест AD26 26.12.2.9: `components=28`, `outlinePoints=42`, `warnings=0`; отчёт Board Information: `Components on board, 28`; `40216536 IU → 102.15 mm`, `40314961 IU → 102.4 mm` | Чтение компонентов, контура и пары IU/мм |
| C-71 | Команда `EcadMcadBridge:ExportSmokeSnapshot` в `PCB → Tools` | `.ins` + исправленный `.rcs` (`MNPCB_Tools10`); UIA-дерево AD26 содержит `Export Smoke Snapshot` под `GUIPanelControls.TxpBarSubMenuControl` | Регистрация одной команды без ручных правок кода |
| C-72 | `DXP.ServerPanelForm`; `DXP.ServerPanelView(DXP.ServerPanelForm form, string name, string caption)`; `void DXP.ServerModule.AddServerView(object view)` и `void DXP.IClient.AddServerView(object view)` | Метаданные `Altium.SDK.dll` / `Altium.SDK.Interfaces.dll`; `framework/shim/Panel.cs`; лог загрузки `BOOTSTRAP_MODULE` и панель `ECAD-MCAD Bridge` в UIA | Минимальный UI-слой на WinForms, старт сервера при загрузке AD (R-01, R-09) |

Reflection и `dynamic` в каркасе не используются. Прямой типизированный вызов `GetCurrentPCBBoard()`
(C-18) подтверждён успешным выполнением в хосте; обращение к внутреннему методу
`Internal_GetCurrentPCBBoard()` не требуется и запрещено правилами репозитория.

## 3. Требует проверки

| ID | Вопрос | Почему не подтверждено | Как проверить |
|---|---|---|---|
| V-02 | ~~Какие reference assemblies использовать: kit 4.0.1 или runtime-сборки AD26?~~ | Закрыт в `BRIDGE-000001`: сборка из `$(AltiumSdkDir)` (runtime AD26, версия `Altium.SDK` 1.0.7383.26074) собирается и загружается в AD26; kit 4.0.1 остаётся контрактом сигнатур, а не reference assembly | См. C-66…C-72, report `BRIDGE-000001`, раздел R-02 |
| V-03 | ~~Работает ли прямой типизированный вызов getCurrentPCB без reflection в AD26?~~ | Закрыт в `BRIDGE-000001`: `PcbSnapshotReader` вызывает типизированный helper `GetCurrentPCBBoard()` (C-18), smoke-тест прошёл, reflection не используется | См. C-18, C-70 и report `BRIDGE-000001`, раздел R-03 |
| V-04 | ~~Как получить активный документ без reflection?~~ | Закрыт в `BRIDGE-000001` для текущей команды: `view.GetOwnerDocument()` (C-67) и `GetKind()` (C-68) работают в хосте | См. report `BRIDGE-000001`, раздел R-04 |
| V-05 | ~~Как определить несохранённые изменения и хеш `.PcbDoc`?~~ | Закрыт в `BRIDGE-000007` на AD26.12.2.9: сохранённый PCB — `GetFileName()` absolute path, `GetModified=false`; добавленный в память track — `GetModified=true`; `DoFileSave` → `true`, затем `GetModified=false`; новый несохранённый PCB — `GetFileName()` = `PCB1.PcbDoc` (не absolute), экспорт отклонён. `documentSha256` вычисляется из стабильного файла `.PcbDoc`, не из snapshot | C-89; report `BRIDGE-000007` |
| V-06 | Как извлекать 3D-модели и высоты для MCAD? | `SaveModelToFile`, `DefaultPCB3DModel` объявлены, поведение и формат не проверены | Отдельная задача MCAD-фазы; не использовать до проверки |
| V-07 | Стабилен ли `SourceUniqueId` между сессиями и ECO? | Не проверено; возможны пустые/меняющиеся значения | Экспорт одного проекта до/после ECO, сравнение |
| V-08 | Как получить толщину платы и стек слоёв? | Частично закрыт в `BRIDGE-000003`: `GetState_MasterStack().Get_ZTop/Get_ZBottom` дали ненулевые значения на vendor-платах (`790552 IU`, `313581 IU`). Точная семантика Z-значений и порядок stackup не подтверждены | Сравнить с Layer Stack Manager; при необходимости отдельный ADR |
| V-09 | Как корректно перечислять механические слои и их назначение? | Частично закрыт в `BRIDGE-000003`: `LayerIterator` + V6-ID дают Top/Mid/Bottom/Plane/Mechanical; механические слои классифицируются по ID, не по имени. Назначение конкретного mechanical-слоя не формализовано | Smoke-тест на плате с именованными mechanical-слоями |
| V-10 | Семантика `GetState_Height()` и bounding box компонента | Не проверено, в каких единицах и с учётом чего считается | Сравнить с UI Altium на эталонной плате |
| V-11 | Автоматизация установки расширения и правки `ExtensionsRegistry.xml` | Известен только ручной путь | Отдельная задача; учесть права администратора и блокировку файла |
| V-12 | Лицензирование и допустимость поставки SDK-сборок Altium вместе с Add-in | Требуется проверка лицензии; в репозиторий DLL не коммитятся | Юридическая/техническая проверка, решение в `DECISIONS.md` |
| V-13 | Точный build AD26 и runtime на целевом стенде | Частично закрыт в `BRIDGE-000001`: `X2.EXE` 26.12.2.9, `X2.runtimeconfig.json` → `tfm=net10.0`, `Microsoft.NETCore.App`/`Microsoft.WindowsDesktop.App` 10.0.10, CLR в логе Add-in — 10.0.10. Конкретный вариант `Altium.SDK.dll` внутри хоста по-прежнему не изолирован | Зафиксированы версия файла, runtimeconfig и лог загрузки; изоляция сборки — отдельная задача |
| V-14 | API SOLIDWORKS 2026 (interop, версия, загрузка Add-in) | Ничего не проверено; в проекте нет кода | Отдельная задача MCAD-фазы |
| V-15 | Поддержка WPF/WinForms внутри AD26 и тема оформления | Частично закрыт в `BRIDGE-000001`: хост-диалог `DXP.Utils.ShowInfo` (C-69) и минимальная WinForms-панель (C-72) работают в AD26; полноценные панели не создавались | Отдельный smoke-тест полноценной панели; для текущей команды кастомный UI не нужен |
| V-16 | Требования к потоку и COM-апартаменту для вызовов PCB API | Не проверено; возможны UI-thread-only ограничения | Smoke-тест с фоновым потоком и без него |
| V-17 | Поведение `TIterationMethod.eProcessAll` на всех типах объектов | Значение известно, полный набор объектов не проверен | Сравнить счётчики с UI/отчётом Altium |
| V-18 | Как различать signal и internal plane в Core `LayerKind`? | Core не имеет отдельного `Plane`; adapter классифицирует оба как `Copper` | Отдельное расширение Core или дополнительный слой метаданных |
| V-19 | Как представлять отверстия падов, pin designator и локальные координаты пада? | В Core 1.0.0 нет соответствующих полей; adapter не подменяет данные | Отдельное решение/ADR до расширения Core |
| V-20 | Как представлять oval и custom pad shapes? | `eRounded` с разными размерами отображён в rounded rectangle с предупреждением; `eCustomShape` не имеет вершин в API | Отдельная задача по PadShape; сейчас ограничение зафиксировано |
| V-21 | Как сохранять порядок слоёв stackup и наличие 3D-модели? | Core не содержит этих полей; adapter их не заполняет | Расширение Core отдельным решением |
| V-22 | ~~Поддержаны ли внутренние вырезы контура на реальной плате?~~ | Закрыт в `BRIDGE-000003-FIX-004`, уточнён в `FIX-005`: `board with cutout.PcbDoc` (SHA256 `ED624C…`) содержит 5 `eRegionKind_BoardCutout`; `ClipWithPolyRegShapes(false,false)` вернул 2 cutout-контура / 16 сегментов, экспортированы 2 hole-контура (7 линий; 6 линий + 3 дуги), все вершины внутри outer. FIX-005 исправил дедупликацию и Region matching, а также трактовку дуг CCW с переходом через 0°; после этого оба выреза сохраняются. `GetState_IsHole` остаётся 0, источник — clip result | См. C-85…C-88, reports `BRIDGE-000003-FIX-004` и `BRIDGE-000003-FIX-005` |
| V-23 | Почему `IPCB_Pad.DefinitionLayerIterator` иногда возвращает V7-ID, не совпадающие с V6? | Adapter обходит это перебором copper-слоёв board layer iterator; высокие V7-ID не используются как V6 | Отдельное исследование V7 layer mapping при необходимости |
| V-24 | Гарантирует ли `IPCB_Polygon.GetState_Segments()` все циклы Board Outline? | Нет: на `PC4-SODIMM` циклы не замкнулись, adapter перешёл на line-fallback для geometric contour. `GeometricPolygon` остаётся источником количества/ролей; Board Cutout берётся из `ClipWithPolyRegShapes`, а не из этого API | Отдельное исследование API сегментов при необходимости |
| V-25 | Можно ли достоверно отличить `eRotatedRectShape`/`eArcShape`/`eTerminator` от поддерживаемых форм? | Семантика не подтверждена; adapter не преобразует их в круг/прямоугольник и выдаёт warning | Отдельный эксперимент с известными pad shape |

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
| 2026-11-24 | BRIDGE-BOOTSTRAP-003: уточнён коэффициент IU→мм и источник сведений об InteractiveBom | Bootstrap |
| 2026-11-24 | BRIDGE-BOOTSTRAP-005: исправлено происхождение runtime-сборок — установленные extension AD26 и копия `publish_out`, не архив; уточнены V-02/V-13 | Bootstrap |
| 2026-11-24 | BRIDGE-000001: добавлены C-66…C-72 и доказательства хоста; закрыты V-02/V-03/V-04, уточнены V-13/V-15 | local-agent |
| 2026-11-24 | BRIDGE-000002: новых Altium API нет; Core-модель, JSON и валидатор не используют SDK | local-agent |
| 2026-11-24 | BRIDGE-000003: добавлены C-73…C-84 и ограничения V-18…V-23; Adapter проверен в AD26.12.2.9 | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-001: geometric contours сохраняются все, fallback диагностируется; уточнены TShape V-24/V-25 | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-002: exact outline matcher отвергает partial/ambiguous; cutout-файл оператора без вырезов, V-22 открыт | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-003: arc angular/midpoint matching; новый cutout-файл снова 0 holes, V-22 открыт | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-004: добавлены C-85…C-88 (IPCB_Region, ClipWithPolyRegShapes, cutout shapes); реальный Board Cutout подтверждён, V-22 закрыт | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-005: исправлены dedup по geometric.Points, Region matching с uniqueness/association и трактовка дуг CCW при переходе через 0° | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-006: analytic arc bounds, geometry-first Region matching, отдельный approximation tolerance для дуговых Hole | local-agent |
| 2026-11-24 | BRIDGE-000003-FIX-007: scale-independent matched-centroid dedup для дуговых Hole | local-agent |
| 2026-11-24 | BRIDGE-000007: C-89 document path/modified API; V-05 закрыт, Altium Package Export | local-agent |
| 2026-11-24 | BRIDGE-000009: C-90…C-93 tracks/vias/regions/texts; Q-010 частично открыт (region arc fallback) | local-agent |
