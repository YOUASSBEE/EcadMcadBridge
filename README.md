# ECAD-MCAD Bridge

Проект интеграционного слоя между electronic CAD (ECAD) и mechanical CAD (MCAD):
два независимых CAD Add-in и общая библиотека Core с версионируемым форматом обмена.

## Назначение

- устранить ручной перенос геометрии и посадочных мест между Altium Designer и SOLIDWORKS;
- сделать обмен детерминированным, проверяемым и воспроизводимым;
- сохранить прослеживаемость каждой передачи данных;
- обеспечить возможность развития обмена в обе стороны без переделки архитектуры.

> **Текущий этап:** `BRIDGE-000020` — подтверждённая полнота контура платы и вырезов:
> `outline.completeness`, геометрическая классификация Board Cutout/Clip контуров,
> origin платы, объём по каждому телу и строгую tessellation. Capability `board-3d-model`
> разрешён только при `complete`; старые пакеты имеют `Unknown`.
> Board-only STEP/native `board.SLDPRT` и Top placement добавлены в `BRIDGE-000019/000018`.

## Согласованный стек

| Компонент | Версия |
|---|---|
| Altium Designer | 26 |
| Altium SDK | 4.0.1 |
| .NET SDK | 10 |
| Target Framework | Altium: `net10.0-windows`; Core/Core.Tests: `net10.0`; SOLIDWORKS backend/tests/smoke: `net10.0-windows`, x64 |
| Язык | C# |
| SOLIDWORKS | 2026 SP3.2, interop 34.3.2.3 |

Совместимость Altium Designer 26, Altium SDK 4.0.1 и .NET SDK 10 с `net10.0-windows`
подтверждена оператором. Целевой TFM нашего Altium Add-in — `net10.0-windows`.

Источник InteractiveBom по заданию `BRIDGE-BOOTSTRAP-004` — архив `InteractiveBom.zip` с полным исходным проектом:

- `InteractiveBom.sln` содержит четыре проекта: `InteractiveBom` (загрузочный shim и COM-фабрика),
  `InteractiveBom.Impl` (реализация извлечения данных платы и команд), `InteractiveBom.Panels`
  (UI-панели) и `InteractiveBom.Tests` (тесты);
- `PluginServerModule.cs`, `Commands.cs`, `Implementation.cs`, `Installation/InteractiveBom.ins`
  и `Installation/InteractiveBom.rcs` — исходники фабрики/модуля, команд и регистрации в Altium;
- `Directory.Build.props` выбирает TFM по `X2.runtimeconfig.json` установленного Altium:
  AD 26.1 → `net8.0`, AD 26.12 → `net10.0`;
- указанная Release-сборка архива находится в `bin/Release/net10.0-windows`.

Отдельная локальная бинарная поставка `InteractiveHtmlBom_ADPlug-master/publish_out/InteractiveBomPlug.dll`
(версия 1.0.0.0, `net8.0-windows`) не смешивается с Release-сборкой архива и не определяет TFM Bridge.
Загрузка в Altium — через `.Ins`/`.rcs` и COM-фабрику
`CSharpPlugin.PluginFactory.InvokePluginFactory(DXP.IClient)`.

SOLIDWORKS 2026 backend реализует STEP import/native SLDPRT и Top-прототип
placement (D-020/D-022). Bottom открыт только как research D-023; production
Bottom остаётся Unsupported до отдельного ADR. Полноценный Add-in host, Registry
и установщик не реализуются. Изменение стека или TFM — только через `.agent/DECISIONS.md`.

## Архитектура (кратко)

```text
Altium Add-in ──► Core ◄── SOLIDWORKS Add-in (будущая фаза)
                   │
                   └──► Exchange Package (JSON + checksums)
```

- `EcadMcadBridge.Core` — домен, валидация и формат пакета; не зависит от CAD SDK.
- `EcadMcadBridge.Altium` — ECAD Add-in; bootstrap, adapter, application, UI.
- `EcadMcadBridge.SolidWorks` — backend STEP import/native SLDPRT и Top placement;
  полноценный Add-in host не реализуется.
- Add-in не ссылаются друг на друга.
- Обмен идёт только через версионируемый файловый пакет Core.
- Все линейные величины из Altium IU сохраняются парой: исходное целое IU и вычисленные мм
  (1 IU = 0.00000254 mm); неподтверждённые значения остаются `null`.
- Git используется для задач, отчётов, ревью и кода; пользовательские PCB, STEP и рабочие сборки
  передаются вне Git через локальный runtime-каталог.

Полная архитектура: [`.agent/ARCHITECTURE.md`](.agent/ARCHITECTURE.md).

## Структура репозитория

```text
AGENTS.md                      правила работы локального агента
README.md                      этот файл
.agent/
  PROJECT_CONTEXT.md           границы, стек, статус, словарь
  ARCHITECTURE.md              компоненты, формат, Git-протокол
  DEVELOPMENT_RULES.md         инженерные и Git-правила
  SDK_API_LEDGER.md            подтверждённые и проверяемые API
  DECISIONS.md                 архитектурные решения (ADR)
  tasks/                       постановки задач
  reports/                     отчёты об исполнении
  reviews/                     ревью
```

Git-протокол разработки (задачи, отчёты, ревью и код) описан в разделе 8 `ARCHITECTURE.md`.
Файловый runtime-обмен между CAD-системами описан в разделе 9 `ARCHITECTURE.md` и не использует Git.
См. также [`.agent/tasks/BRIDGE-000001.md`](.agent/tasks/BRIDGE-000001.md).

## Незыблемые ограничения

- DelphiScript и Legacy API Altium не используются.
- Неподтверждённые API не попадают в категорию подтверждённых.
- `.PcbDoc` не разбирается вручную; чтение — только через `PCB.IPCB_*`.
- Запись в `.PcbDoc` на этапе `BRIDGE-000001` запрещена.
- Ветка `source` изменяется только через Pull Request.
- В репозиторий не коммитятся проприетарные DLL, лицензии, секреты и данные заказчиков.

## Сборка и проверки

Пути к SDK не коммитятся. Перед сборкой создайте `Directory.Build.local.props` рядом с
`Directory.Build.props` (файл уже в `.gitignore`):

```xml
<Project>
  <PropertyGroup>
    <AltiumSdkDir>E:\Program Files\Altium\AD26\System</AltiumSdkDir>
  </PropertyGroup>
</Project>
```

Затем:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

`dotnet build -c Release` должен пройти с нулём предупреждений (include `TreatWarningsAsErrors=true`).

## Top placed assembly (BRIDGE-000018)

Smoke размещает Top/Top Generic Embedded bodies из Exchange Package в native
`.SLDASM` с реальными transforms:

```powershell
$env:ECAD_MCAD_BRIDGE_PACKAGE_ROOT = '<absolute package root>'
dotnet run --project tools/EcadMcadBridge.SolidWorks.Smoke -c Release -- `
  --placed-assembly `
  --package '<package directory>' `
  --template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot' `
  --output "$env:TEMP\EcadMcadBridge\BRIDGE-000018\smoke" `
  --designators 'U101,U102,U103,U104'
```

`--designators` требует все перечисленные designators. Без него выбираются первые
`--max-components` (по умолчанию 20) поддержанных component instances в
детерминированном порядке. Режим `--assembly`/`--sample`/`--only-asset` сохранён.
Результат — `smoke-result.json` с package ID, mapping, requested/readback matrices,
residuals, lifecycle и cleanup. Production Bottom (research — D-023), projection
mismatch, Native/External и board solid не поддерживаются.

## Установка Altium Add-in (текущий стенд)

1. Скопируйте из `src/EcadMcadBridge.Altium/bin/Release/net10.0-windows/` в каталог
   расширений AD26 (`...\Extensions\EcadMcadBridge\`):
   `EcadMcadBridge.dll`, `EcadMcadBridge.Core.dll`, `EcadMcadBridge.deps.json`,
   `Installation\EcadMcadBridge.ins` и `Installation\EcadMcadBridge.rcs`
   (`.ins`/`.rcs` копируются в корень каталога расширения).
2. В `ExtensionsRegistry.xml` добавьте/проверьте элемент `Item` с `HRID=EcadMcadBridge`,
   `Guid=f2c9b1a4-6d5e-4c8b-9a31-7e0f2b8d4c10`, `Path` на каталог из шага 1 и `Status=0`.
   Перед правкой сделайте резервную копию файла.
3. Перезапустите Altium Designer. В открытом `.PcbDoc` появятся `PCB → Tools → Export Smoke Snapshot`
   и `PCB → Tools → Export Board Snapshot`.
4. `Export Board Snapshot` создаёт Exchange Package:
   `<package-root>/<package-guid>/{manifest.json,board.snapshot.json,checksums.sha256}`.
   Плата должна быть сохранена; при `GetModified()==true` или отсутствии absolute path
   экспорт отклоняется.
5. Package root: `ECAD_MCAD_BRIDGE_PACKAGE_ROOT` (absolute), иначе
   `%LOCALAPPDATA%\EcadMcadBridge\Packages`. `Export Smoke Snapshot` остаётся
   диагностическим JSON в `%TEMP%`. Лог — в `%TEMP%\EcadMcadBridge\logs\bridge.log`;
   absolute user paths в него не пишутся.
   В snapshot не попадает содержимое пользовательской платы сверх согласованной модели.

Автоматизация шага 2 и перенос расширения на другой стенд не реализованы в этой задаче
(открытый вопрос O-006). Smoke-тест выполняется вручную и фиксируется в report задачи.

Результат smoke-теста на синтетической плате `board.PcbDoc`: `components=28` (совпадает с
`Board Information → Components on board` в AD26), `outlinePoints=42`, `warnings=0`,
контрольная точка `40216536 IU → 102.15 mm`. Команда запускалась дважды без перезапуска AD,
`.PcbDoc` остался неизменным (SHA256 до и после: `EC13CC84307C6393263624A1A2F4C10DDB767640B2860C3E71E655098A87E031`).

## Документы

| Документ | Назначение |
|---|---|
| [AGENTS.md](AGENTS.md) | Обязательные правила для локального агента |
| [PROJECT_CONTEXT.md](.agent/PROJECT_CONTEXT.md) | Контекст, границы, статус, открытые вопросы |
| [ARCHITECTURE.md](.agent/ARCHITECTURE.md) | Архитектура, формат пакета, Git-протокол, runtime-обмен |
| [DEVELOPMENT_RULES.md](.agent/DEVELOPMENT_RULES.md) | Инженерные правила, DoD, Git |
| [SDK_API_LEDGER.md](.agent/SDK_API_LEDGER.md) | Доказательная база API |
| [DECISIONS.md](.agent/DECISIONS.md) | ADR и открытые решения |
