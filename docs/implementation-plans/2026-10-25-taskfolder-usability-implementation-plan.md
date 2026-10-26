---
title: "TaskFolder: план развития удобства и функциональности"
created: "2026-10-25"
plan_version: "1.0"
status: "ready-with-assumptions"
planning_mode: "brownfield"
baseline: "TaskFolder 1.2.9, исходники без .git; контрольные суммы в разделе 2"
target: "coding-agent"
ui_scope: "included"
---

# TaskFolder: план развития удобства и функциональности

## 1. Цель, границы и готовность

Сделать TaskFolder удобнее для ежедневного запуска и организации приложений, файлов, папок и ссылок, сохранив существующий WinForms-интерфейс, taskbar widget, tray и portable mode.

Пользователь одобрил все предложения из оценки приложения, **кроме рабочих наборов, запускающих несколько элементов одной командой**. План охватывает избранное, менеджер ярлыков, умный поиск, удобное добавление, восстановление, экспорт/импорт и перечисленные небольшие доработки.

**Readiness: Ready with explicit assumptions.** Основные потоки и формат данных исследованы; архитектурных решений от пользователя для начала разработки не требуется. Сначала исполнитель должен найти доступный SDK и повторно проверить исходники. В текущем окружении сборка не подтверждена. Реальная Windows Shell integration и визуальные состояния требуют отдельных runtime-проверок.

Вне scope: рабочие наборы/сцены и массовый запуск, cloud sync, AI-функции, собственный updater, смена UI framework, обновление production dependencies/runtime, новые способы встраивания в taskbar, настройка Defender, изменение startup/Windows registration. Работа над планом не является разрешением на реализацию, установку, миграцию пользовательских данных, commit/push или публикацию.

### Принятые обратимые defaults

| Решение | Default | Владелец и точка проверки |
| --- | --- | --- |
| Область поиска и избранного | Активный профиль; межпрофильный поиск в этот scope не входит | Orchestrator, S3/S6 |
| Существующие Recent / Most Used | Сохранить; добавить независимые настройки их видимости | Orchestrator, S3 |
| Видимость избранного | Первые 8 прямо в меню; остальные через «Всё избранное»; число сохранённых не ограничивать этим порогом | UI executor, S3, оценить высоту меню на 1080p |
| Языки | System / Русский / English; новые установки System, мигрированные сохраняют English до выбора пользователя | UI executor, S8 |
| Темы | System / Light / Dark; новые установки System, мигрированные сохраняют Light | UI executor, S8 |
| Hover widget | 100–1500 ms, шаг 50 ms, default 300 ms | Widget executor, S9 |
| Блокировка widget | Default false для сохранения существующего перетаскивания | Widget executor, S9 |
| Восстановление | Без автоматического удаления сохранённых элементов; очистка пользователем с показом состава и размера | Orchestrator, S2 |
| Импорт | Создать отдельный профиль; объединение — только после preview; без скрытого overwrite | Backup executor, S7 |
| Зависимости | .NET/WinForms и существующие Windows APIs; без новых NuGet packages по умолчанию | Orchestrator, каждый этап |

Эти defaults можно уточнить при реализации без пересмотра архитектуры. Изменение разрешений, владения данными, совместимости или стоимости требует отдельного решения.

## 2. Baseline и доказательства

### Состояние проекта на 25 октября 2026

- `TaskFolder.csproj`: `net8.0-windows`, Windows Forms, `PerMonitorV2`, версия `1.2.9`, без `PackageReference`.
- Текущий source folder не содержит `.git`; `git status` и `git remote -v` завершились сообщением `not a git repository`. Branch, HEAD, remotes и dirty state установить нельзя. Не выполнять `git init`, создание branches или перенос в другой checkout без запроса пользователя.
- `dotnet --info` для системного `dotnet` показал host `10.0.11`, Windows x64 и отсутствие SDK в этом toolchain. Есть Windows Desktop Runtime `8.0.11`, но runtime не заменяет SDK. Наличие SDK в других каталогах не проверялось.
- В этом source folder нет `.github/workflows`. Не предполагать наличие удалённого CI или автоматического release pipeline.
- Исследованы основные source units, project manifests и существующие smoke tests. Build/tests, GUI, Shell app catalog, импорт и installer в ходе планирования не запускались.

### Подтверждённые ограничения

| Область | Evidence на baseline | Последствие |
| --- | --- | --- |
| Metadata identity | `Services/SettingsService.cs:138`, `:153`, `:175`: ключ обрезается до filename; `Services/ShortcutManager.cs:154` использует этот ключ | Одинаковые имена в разных папках/профилях смешивают metadata; исправить до новых функций |
| Запись | `Services/SettingsService.cs:109–130`: два отдельных JSON; `AtomicWrite` делает `File.Copy(..., overwrite: true)` и только логирует ошибку | Нужны согласованная запись и явный failure result; README описывает другой алгоритм |
| Rename | `Services/ShortcutManager.cs:419–449`: metadata меняется до `File.Move` | Возможна частичная операция; нужен единый координатор |
| Перестановка | `Program.cs:1119`: `OnMoveShortcut`; сохранение выполняется для двух элементов | Сохранять весь новый порядок одной операцией, особенно при нескольких unset SortOrder |
| Поиск | `Program.cs:348–477`: `Contains`/`StartsWith` по display name; Enter использует `_firstSearchMatch` | Расширить matching и отдельно моделировать выбранный результат |
| Добавление | `Program.cs:969`, `Services/ShortcutManager.cs:337`: один файловый выбор; `File.Exists` до создания `.lnk` | Нужны отдельные типы ввода, batch и folder/URL/catalog flow |
| Пропавшие targets | `Services/ShortcutManager.cs:141–169`: невалидный target не добавляется в видимый список | Сохранять запись и показывать состояние для исправления |
| Удаление | `Services/ShortcutManager.cs:401`, `Views/SettingsForm.cs:595`: `File.Delete`, очистка без undo | Ввести recoverable operations; отдельно защитить прямые `.exe` |
| Profiles | `Services/ProfileService.cs:26–91`: профиль определяется именем каталога; `Default` — `Shortcuts/` | Сохранить расположение; отделить identity от имени и валидировать новые имена |
| Menu/Widget | `Program.cs:225`, `Utilities/Windows11Menu.cs:19`, `Utilities/WidgetInteraction.cs:37` | Единый renderer уже есть; palette фиксирована; widget hover 300 ms |
| Existing tests | `tests/PowerMenuSmoke`, `TaskbarPrototypeSmoke`, `WidgetPrototypeSmoke`, `WindowsIntegrationSmoke` | Переиспользовать точечные harnesses; разделять чистые, native и live проверки |
| Packaging | `BuildInstaller.ps1`, `TaskFolder.iss`: folder publish, per-user install | Проверить satellite resources и сохранить этот путь установки |

### Контрольные суммы исходников

SHA256, полученные при планировании; если отличаются, перечитать затронутые units и обновить baseline до редактирования.

```text
TaskFolder.csproj                 C4A08CD71135CDB4C651537B4E6861FB918F3BA91214ECF1B629AD1C487B0B66
Program.cs                        43CBDECDD5257B15D7B93A23E5D88C2F4930D9CFED101EAA792E3CC2F2CFF70C
Models/AppSettings.cs             D174BDE371CF0BE82790D88B122D122AF3119190404B56157F99833C0DDA49BD
Services/SettingsService.cs        8A5DB3395222062669EA0DB66C55ECA633A7BCB5851AB9A54BB4414E1DBB68D2
Services/ShortcutManager.cs        480681CC04419E0A8D75210835A04B4F84F09CE89457AED99D579E2C82EF28A1
Utilities/WidgetInteraction.cs     00EE61554CB0ADF2331C4784D37D6FD9FFA3E2C171B8864203066F3B58728605
```

### Primary documentation

Проверена через web 2026-10-25. Указанные API нужно сверять с `net8.0-windows`; наличие API в более новом runtime не означает доступность для текущего target.

- [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace): замена отдельного файла с сохранением предыдущей копии; это не transaction для нескольких JSON и shortcut files.
- [Resources / .resx](https://learn.microsoft.com/en-us/dotnet/core/extensions/create-resource-files): стандартный механизм локализованных ресурсов.
- [Windows keyboard interaction](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions): предсказуемые focus, arrows и keyboard access.
- [Menus and context menus](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/menus-and-context-menus): доступность часто используемых действий.
- [Known folder IDs](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid) и [SHGetKnownFolderItem](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderitem): `FOLDERID_AppsFolder` — виртуальная Shell folder, не обычный путь для `Directory.GetFiles`.
- [ZIP extraction](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-compress-and-extract-files): проверять нормализованный destination и границу extraction root.
- [WinForms .NET 9 changes](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net90): `Application.SetColorMode` появился в более новом target; не использовать этот API как доступный в .NET 8 и не обновлять framework ради темы автоматически.

## 3. Требования и защищённые инварианты

### Функциональные требования

| ID | Требование |
| --- | --- |
| R01 | Pin/unpin, отдельный порядок избранного, сохранение по профилям и после restart |
| R02 | Менеджер: дерево папок, список, drag reorder/move, multi-select, создание папок, copy между профилями |
| R03 | Search: aliases, несколько слов, опечатки, RU/EN layout fallback, selection arrows/Enter/Escape |
| R04 | Add: установленное приложение, файл, папка, URL, команда; batch и выбор destination |
| R05 | Undo удаления, восстановление metadata, видимость и repair отсутствующих/невалидных targets |
| R06 | Export/import конфигурации с preview, переносом собственных иконок и обработкой конфликтов |
| R07 | Блокировка позиции taskbar widget |
| R08 | Настраиваемая задержка hover widget с сохранением существующего default |
| R09 | Активный профиль виден в главном меню, не только в tray tooltip/submenu |
| R10 | Полная RU/EN localization собственных строк и выбор языка |
| R11 | System/Light/Dark для поверхностей приложения; корректный high contrast |
| R12 | Независимая metadata identity, recoverable persistence, согласованные batch operations |

### Инварианты

- Существующие `.lnk` запускать через Windows Shell без потери arguments, working directory, PWA app identity и custom launch flags. Не подменять их запуском только target executable.
- Существующие `Shortcuts/`, `Profiles/<name>/`, portable data root и `Default` остаются на месте. Не менять startup/Start-menu/Installed apps registration при обычном сохранении preferences.
- Tray остаётся fallback при недоступном taskbar widget. Сохранить привязку к taskbar, DPI, monitor placement, outside-click dismissal, вложенные меню и 200 ms folder hover.
- «Удалить из TaskFolder» не удаляет target приложения, документа или папки. Прямой `.exe` внутри shortcut tree тоже считается защищённым payload; его нельзя удалять как обычный shortcut.
- Copy создаёт новую identity; rename/move через приложение сохраняют identity, статистику, custom icon, aliases и pin. Чужие файлы не перезаписываются.
- Папки, включая пустые и промежуточные, остаются доступны. Не рекурсировать через junction/reparse point за пределы managed tree.
- COM/Shell parsing выполняется в STA (Single-Threaded Apartment); не переносить существующую COM-логику в произвольный `Task.Run`. UI changes выполняются на UI thread.
- Существующий global hotkey, launch notifications, Power submenu, настройки размера текста/строк и custom logo сохраняются.
- Unsaved fields в Add/Manager/Settings не сбрасываются из-за watcher refresh, смены темы или языка. При конфликте показывать выбор/повтор, не применять stale snapshot поверх новых данных.

### Нефункциональные цели

Это новые проверяемые бюджеты, а не результаты измерения текущей версии:

- Для fixture из 1000 shortcuts: warm search p95 ≤100 ms, warm menu open p95 ≤200 ms на зафиксированном reference PC. Измерять отдельно от initial disk scan и Shell icon extraction; при промахе оптимизировать и повторить замер.
- Search работает по memory snapshot; не читает links, сеть и registry на каждое нажатие. Background catalog loading отменяемый; UI показывает progress/partial results.
- Один writer на data root, проверка Revision перед commit, отсутствие lost updates от UI и watcher. Нет фонового polling всех установленных apps.
- Для ordinary updates повреждение основного JSON не должно оставлять пользователя без последнего валидного состояния. Для file batches восстановление определяется journal.
- Windows 10/11 в рамках текущей заявленной совместимости, scaling 100/150/200%, keyboard-only и screen-reader checks. Недоступную платформу записывать как gap, не выдавать static analysis за её проверку.
- Без новых сетевых сервисов, telemetry, платежей и обязательных production dependencies. Нет заявлений о RAM/CPU из README без измерений.

## 4. Предлагаемая архитектура

Сохранить WinForms и файловые shortcuts. Выделить операции, поиск и persistence из `Program.cs`, чтобы главное меню и менеджер использовали одинаковую бизнес-логику.

```text
Tray/menu / Manager / Add / Recovery / Import preview
                    |
           ShortcutOperationService
          /           |             \
LauncherStateStore  ShortcutManager  ShortcutLaunchService
state.v2.json       tree + watcher   Shell dispatch
    |                  |
Migration / journal / Recovery / assets

ShortcutSearchService -> memory snapshot -> selected SearchResult
InstalledAppCatalogService -> STA Shell discovery -> Add draft
LocalizationService + UiThemeService -> existing/new WinForms surfaces
BackupService -> validated staging -> operation coordinator
```

Предлагаемые новые units — границы ответственности, а не обязательные отдельные public frameworks:

- `Models/LauncherState.cs`, `ShortcutEntry.cs`, `ProfileState.cs`, `FolderState.cs`, `OperationResult.cs`, `SearchResult.cs`.
- `Services/LauncherStateStore.cs`, `StateMigrationService.cs`, `ShortcutOperationService.cs`, `ShortcutLaunchService.cs`, `ShortcutSearchService.cs`, `InstalledAppCatalogService.cs`, `BackupService.cs`, `LocalizationService.cs`, `UiThemeService.cs`.
- `Utilities/SafePath.cs`, `Utilities/ShellAppCatalog.cs` для Windows-specific parsing.
- `Views/ShortcutManagerForm.cs`, `AddItemDialog.cs`, `RecoveryForm.cs`, `ImportPreviewForm.cs`.
- `Resources/Strings.resx`, `Resources/Strings.ru.resx`.

### Существенные решения

| Решение | Рекомендация | Альтернатива и причина отказа |
| --- | --- | --- |
| Identity | UUID записи + ProfileId + отдельный path binding | Только relative path устраняет filename collision, но ломает metadata при move/rename |
| Persistence | Один versioned state snapshot для settings и metadata; journal для операций с файлами | Два JSON без transaction сохраняют риск частичного успеха; SQLite сейчас добавляет ненужную миграцию и dependency |
| Runtime | Сохранить `net8.0-windows` в scope этого плана | Runtime migration — отдельная работа с отдельной совместимостью; не маскировать её под темы |
| UI | Существующий menu для запуска + отдельное окно организации | Все операции внутри transient menu усложняют multi-select, drag и recovery |
| Import | Manifest + whitelist payload + staging + preview | Raw unzip прямо в data root не даёт безопасного preview и конфликтов |
| Themes | Параметризовать существующую palette и тематизировать собственные controls | Framework rewrite или новая theme library не обоснованы scope |

## 5. Данные, контракты и обработка ошибок

### 5.1. State v2

`state.v2.json` — новый источник settings/metadata; `.lnk/.url` и существующие файлы остаются источником Shell launch behavior. Legacy `settings.json` / `metadata.json` сохраняются для migration/recovery; после cutover новая версия их не перезаписывает и не делает dual-write.

| Entity | Обязательные поля и правила |
| --- | --- |
| Root | `SchemaVersion=2`, `Revision:int64`, `Profiles[]`, `Folders[]`, `Shortcuts[]`, `Settings`, `LegacyAmbiguities[]`; неизвестная future schema открывается без записи с понятной ошибкой |
| Profile | `Id:Guid`, `Name:string`, `RelativeRoot:string`; Default имеет постоянную identity и прежний root; display name не используется как identity |
| Folder | `Id:Guid`, `ProfileId:Guid`, `RelativePath:string`, `SortOrder:int`; поддержка пустых папок |
| Shortcut | `Id:Guid`, `ProfileId:Guid`, `RelativePath:string`, `Kind:ShellLink/Url/Executable`, `Aliases:string[]`, `IsPinned:bool=false`, `PinnedOrder:int`, `SortOrder:int`, `LaunchCount:int64=0`, `LastUsedUtc:DateTimeOffset?`, `CustomIcon`, `Suppressed:bool=false` |
| CustomIcon | `Source:ManagedAsset/ExternalReference`, relative asset path либо существующий внешний path, index; не копировать внешние DLL/EXE целиком ради иконки |
| Settings | Существующие свойства + `Language`, `Theme`, `WidgetPositionLocked`, `WidgetHoverDelayMs=300`, `ShowRecent=true`, `ShowMostUsed=true`; сохранить совместимые численные ranges текущих font/row settings |
| LegacyAmbiguity | Original legacy record без потери полей + candidate IDs + resolution state; для неразличимых filename collisions |

`Ready/MissingTarget/Unavailable/Invalid/NeedsReview` — вычисляемый статус записи. Missing target не означает автоматическое удаление. UNC/removable path получает `Unavailable`, пока проверка не завершилась или носитель недоступен; не блокировать меню ожиданием сети. Shell items/packaged apps проверяются через Shell identity, не через `File.Exists` виртуального пути.

Нормализация path binding: Windows ordinal case-insensitive comparison, проверка полного пути и границы managed root, запрет `..`, rooted destination, ADS, reserved names и выхода через reparse points. Не менять Unicode-написание реальных имён автоматически. Target вне managed root разрешён как ссылка, но запрещён как destination удаления/перемещения.

Внешние изменения в Проводнике поддерживаются: обработчик `Renamed` сохраняет old/new mapping; watcher overflow вызывает полный reconcile. При offline rename с неоднозначным matching не объединять записи по одному target автоматически: показать NeedsReview, сохранить старую metadata до решения. Полной реконструкции истории внешних изменений из одного имени файла не обещать.

### 5.2. Миграция и совместимость

1. Прочитать legacy JSON и все profile roots; построить dry-run report без записи. Считать также missing targets и пустые folders.
2. Показать пользователю состав изменения, snapshot path, размер и recovery route до применения к его данным. В ходе разработки использовать только fixtures. Создание существенной копии данных без согласия не выполнять.
3. После согласия сохранить legacy JSON и явный manifest managed links/assets в отдельном migration snapshot. Не копировать targets, runtime и установленные программы. Прямые `.exe` на этом шаге не перемещать и не менять.
4. Построить v2 state в staging. Однозначный basename переносит metadata в одну запись. Для коллизий сохранить original record в `LegacyAmbiguities`, создать независимые entries с defaults и дать пользователю позднее распределить metadata. Не размножать LaunchCount и не приписывать shared статистику произвольному ярлыку.
5. Проверить counts, IDs, launch definitions, paths и сумму `assigned + unresolved` legacy counters. Временные отметки сохранить вместе с original representation, если timezone старого значения неоднозначен.
6. Записать валидный `state.v2.json` через temp-файл в том же каталоге и replace/move; проверить чтением. Существующие shortcut trees не перемещать.
7. Только после успешной записи переключить consumers на v2. Повторный запуск миграции idempotent: валидный v2 не мигрировать повторно.
8. Незавершённая migration, corrupt primary, locked file, disk full и future schema оставляют legacy/last-good копию читаемой. Не сохранять пустые defaults поверх исходника.

Одновременная запись старой и новой версией не поддерживается. Проверить существующий mutex/activation path; upgrade выполняется с остановленным старым процессом. Downgrade: остановить новую версию, сначала экспортировать новые данные, затем по отдельному разрешению восстановить migration snapshot/старые bindings и запустить старую версию. После v2 file mutations простой запуск старого EXE не является безопасным rollback. Исходные `.exe` и recovery payload не удалять.

### 5.3. Mutation contract

Все UI mutations используют `ShortcutOperationService`; `SettingsService` остаётся facade на время перевода call sites.

```text
OperationRequest: OperationId Guid, ExpectedRevision int64, ProfileId Guid,
                  SelectedIds Guid[], Payload
OperationResult: Success | Cancelled | ValidationError | Conflict |
                 AccessDenied | StorageUnavailable | IoFailure | UnsupportedSchema
                 + OperationId, NewRevision?, ItemErrors[], UndoToken?
CatalogChanged: Revision, ProfileId, ChangedIds, ChangeKind
```

- Mutations: `AddItems`, `RenameItem`, `MoveItems`, `CopyItems`, `ReorderItems`, `CreateFolder`, `Pin`, `Unpin`, `RemoveItems`, `RestoreOperation`, `RepairTarget`, `ApplyImport`, `SavePreferences`.
- Общая validation и errors; текст локализуется в UI. Ошибка записи не возвращает Success.
- Один serial writer, staging и durable journal: `Prepared -> FilesApplied -> StateCommitted -> Completed`; compensation/recovery восстанавливает последний согласованный state. OperationId предотвращает повторное применение завершённой команды.
- Пакет заранее валидируется целиком. При внешней правке между preview и commit — Conflict и новый preview; без молчаливого overwrite. Отмена доступна до commit. После commit — явная компенсирующая операция.
- После commit одно согласованное уведомление `CatalogChanged`. Watcher events от собственной операции coalesce; внешние изменения не теряются. UI сверяет Revision, обновляет только затронутые области и сохраняет focus/selection/drafts.
- Copy между профилями создаёт новый ID, переносит aliases/icon; launch statistics начинает с нуля, pin по умолчанию выключен. Move/rename сохраняют ID. Порядок группы сохраняется одним batch; порядок pins независим от folder order.

### 5.4. Undo и repair

- Удаление `.lnk/.url`: сохранить bytes и metadata под `Recovery/<OperationId>/`, затем убрать из активного дерева согласованной операцией. Undo восстанавливает exact bytes, ID, order, pin, aliases и icon references.
- Для прямого `.exe` или неизвестного пользовательского payload: только `Suppressed=true`, исходный файл остаётся на месте. Undo снимает suppression. Watcher не должен немедленно вернуть suppressed entry.
- «Remove All» использует тот же контракт. Удаление folder не вызывает безусловный `Directory.Delete(..., recursive:true)`: неизвестное содержимое и targets остаются; папка удаляется только если действительно пуста и входит в подтверждённую операцию.
- Restore проверяет занятый original path: предложить новое имя либо пропуск. Не заменять файл, появившийся после удаления. Очистка Recovery — отдельное подтверждение с размером и перечнем; автоматический срок удаления не вводить.
- Repair: выбрать новый target, предварительно показать сохранённые arguments/working directory. Менять через Shell link editing с сохранением остальных полей. Несовместимая замена типа требует отдельного подтверждения. URL редактируется как URL; unavailable drive не объявлять безвозвратно missing.

### 5.5. Add / launch / installed catalog

- `AddItemDialog`: режимы «Приложение», «Файл», «Папка», «Ссылка», «Команда»; общий выбор profile/folder и preview результата.
- Existing `.lnk` копируется с сохранением bytes, `.url` валидируется, обычный файл/папка получает `.lnk`. URL вводится напрямую, default `https/http`; нестандартный protocol показывается как explicit command, без скрытого выполнения.
- Команда: executable/interpreter, отдельные arguments и working directory. Не применять `Invoke-Expression`, `cmd /c` или PowerShell к произвольному пользовательскому тексту автоматически; не менять execution policy, не повышать права. Scripts запускаются только по явно выбранной launch definition.
- `InstalledAppCatalogService` перечисляет Start-menu shortcuts и Shell `FOLDERID_AppsFolder` на отдельном STA worker. Сначала показать доступные результаты, затем иконки. Не сканировать весь диск или uninstall registry в поиске предполагаемого executable.
- Identity catalog item включает Shell identity/полный `.lnk` и arguments; разные PWA одного browser не схлопывать по `msedge.exe/chrome.exe`. Для packaged app создать Shell-compatible link, подтвердить через изолированный spike в S5.
- Existing imports и catalog selection никогда сами не запускают приложение. Внешний drop обрабатывается в manager/add, не вмешивается в drag позиции taskbar widget. Дубли сравниваются по launch definition с аргументами и destination, а не только TargetPath.
- `ShortcutLaunchService` оборачивает существующий dispatch, даёт тестовый adapter. LaunchCount означает принятый Shell запрос, а не доказательство успешной загрузки приложения. Ошибка сохранения статистики после dispatch не должна показываться как ошибка самого запуска.

### 5.6. Search contract

- `Search(query, activeProfileSnapshot) -> ordered SearchResult[]`; result содержит stable ID, display name, category breadcrumb, MatchKind и ranking score.
- Unicode case-insensitive matching по display name и aliases; несколько слов должны находиться независимо от порядка. Не нормализовать корейский текст через RU/EN keyboard map.
- Порядок: exact name/alias, prefix, все токены, substring, затем ограниченные layout/fuzzy matches. Direct match всегда выше исправления раскладки. Fuzzy применять при достаточной длине запроса, с ограниченной edit distance; короткий запрос не превращать в список случайных совпадений.
- При равном match quality использовать pin, затем recent/frequency, затем стабильный display name/ID. Не прятать качественное совпадение из-за нулевой статистики.
- В editor `Down/Up` переводит выбор по результатам; Enter запускает выбранный ID; при вводе выделяется первый результат; Escape сначала очищает query, повторный Escape закрывает menu. Zero results не допускает stale Enter launch. IME composition не подтверждает запуск преждевременно.

### 5.7. Export/import

- Archive: versioned `manifest.json`, profile/folder metadata, whitelisted `.lnk/.url` bytes и managed icon assets; SHA256 каждого payload. Использовать `System.IO.Compression`.
- Не включать runtime, установленные apps, внешние документы, DLL/EXE с custom icon, secrets/local env автоматически. Для внешней custom icon экспортировать извлечённый bitmap/ICO, если это возможно; иначе manifest предупреждает о внешней зависимости. Embedded `.exe` экспортируется как ссылка/definition с предупреждением о непереносимости, без копирования binary по умолчанию.
- Preview показывает profile mapping, conflicts, missing/external targets, команды/arguments с возможностью раскрыть их и объём payload. Нет autorun при preview/import. Пользователь выбирает какие profiles/items импортировать.
- Default — новый профиль. Merge: skip, rename или явный replace для каждого конфликта; replace recoverable через общий journal. Идентичный повторный импорт одной archive/checksum не создаёт дубли без explicit «создать копию». Новая копия получает новые IDs.
- Первичные лимиты импорта: 10 000 entries, 256 MiB суммарно распакованных данных, 16 MiB на icon asset; streaming quotas, остановка до превышения. Лимиты — defaults дизайна, проверить fixture нагрузки. Архив большой конфигурации не обрабатывается молча частично.
- Проверять schema, checksum, normalized destination, duplicate/case-colliding entries, absolute/UNC/drive paths, `..`, ADS, symlink/reparse payload и выход за staging root. Raw shortcuts не исполнять. Staging не находится внутри активного watcher tree.
- По завершении — единый report импортированных/пропущенных/требующих repair элементов. Ошибка в commit даёт recovery route, а не тихий частичный успех.

## 6. UI handoff

UI deferral **не активирован**: frontend входит в scope будущей реализации.

Сохранить текущую Fluent-стилистику, иконку, логотип и плотность меню. Новые окна используют те же typography, spacing и focus states.

- Главное меню: видимый profile selector/label, search, pins, существующее дерево и utility actions. Recent/Most Used остаются доступными по настройкам.
- Manager: resizable window, folder tree слева, список справа; columns Name/Type/Status, multi-select, search/filter, commands и status результата. Keyboard equivalents для drag/move/pin/remove. Менеджер обновляется после изменения из tray и наоборот.
- Add: понятные типы ввода, выбор destination, batch preview, validation рядом с полем, сохранение draft после ошибки.
- Recovery: список операций и элементов с original folder/date/size, Restore и подтверждаемая Clear. «Требуют внимания» в manager ведёт к repair.
- Import preview: staged state, понятные conflicts и итог перед Apply; Cancel не меняет active data.
- Settings: независимые toggle для lock/hover и Recent/Most Used; hover delay disabled при hover off; язык/тема применяются к собственным открытым окнам без сброса ввода.
- Все новые потоки имеют loading, empty, validation, denied/unavailable, conflict, success и retry states; никаких декоративных controls без handler.
- RU/EN не переводят названия пользовательских профилей, папок, aliases и приложений. «Default» остаётся storage identity; отображаемое название может локализоваться.

## 7. Контроль изменений с влиянием на данные

**Классификация:** локальный single-user app; широкий infrastructure blast radius отсутствует. Persistence cutover и batch file mutations требуют усиленного контроля целостности и rollback, поэтому для них применяется high-impact workflow. Установка/облако в этот план не включены.

| Фаза | Обратимость | Контроль |
| --- | --- | --- |
| Source/tests и fixtures | Полностью обратима | Изолированный DataRoot; исходные пользовательские данные не открывать на запись |
| Migration preview / staged v2 | Полностью обратима | Legacy и shortcut tree неизменны |
| V2 cutover до file mutations | Обратима при остановке приложения | Проверенный snapshot, отключение v2 через восстановление согласованного состояния; не удалять evidence |
| V2 rename/move/remove/import | Условно обратима | Durable journal + Recovery; downgrade после reconciliation |
| Очистка Recovery/snapshot | Точка потери возможности полного восстановления | Только отдельная подтверждённая операция; автоматическая очистка не входит в scope |

### Реестр материальных рисков

| Риск | Вероятность / ущерб | Мера и detection | Владелец / contingency |
| --- | --- | --- | --- |
| Коллизия legacy metadata | Подтверждено структурой / средний | Dry run по всем profiles; unresolved records и reconciliation counters | Orchestrator / ручное распределение без потери original |
| Crash/disk-full в середине mutation | Возможен / высокий | Fault injection на каждом journal transition; last-good state и staged payload | Storage executor / recovery до следующей записи |
| Watcher + UI lost update | Возможен / высокий | Serial writer, Revision, file hash recheck, overflow reconcile | Orchestrator / Conflict, повторный preview |
| Потеря PWA identity / arguments | Возможна / высокий | Byte-preserving `.lnk`; Shell integration fixture и реальная проверка после разрешения | Launch executor / вернуть original link |
| ZIP path traversal / сторонняя команда | Возможен / высокий | Whitelist, quotas, containment, preview, отсутствие autorun | Backup executor / отклонение до commit |
| Dark mode/DPI ухудшает читаемость | Возможен / средний | Theme tokens, high contrast, RU layout, 100/150/200% visual QA | UI executor / Light fallback без потери preferences |
| COM stalls на каталоге/UNC | Возможен / средний | STA worker, cancellation, progressive UI, no network validation в search | Catalog executor / partial catalog и retry |
| Рост Recovery/assets | Возможен / средний | Отображение размера, dedup managed icons, явная очистка | Orchestrator / пользователь решает состав очистки |

Release gates: source review общих контрактов; migration dry run и fault recovery; тесты search/mutations/archive validation; разрешённый native/GUI smoke; готовый runbook rollback. Обычный build не заменяет реальные Shell/PWA/taskbar проверки.

## 8. Последовательность внедрения

Каждый этап — ограниченный diff с результатом и своей проверкой. Переход к зависимому этапу только после review контракта и evidence. Все новые test project paths ниже — планируемые, они пока не созданы.

### S0. Воспроизводимая среда и изолированная проверка

- **Результат:** разработка и тесты не пишут в реальную конфигурацию TaskFolder.
- **Paths/symbols:** `TaskFolder.csproj`, constructors `SettingsService`, `ShortcutManager`; новые `tests/CoreSmoke/CoreSmoke.csproj`, `tests/CoreSmoke/Program.cs`, `tests/fixtures/` только для небольших синтетических входов.
- **Работа:** найти существующий SDK, пригодный для текущего target; при отсутствии предложить установку отдельно, без автоматического system-wide install. Сверить hashes; найти актуальный checkout только если пользователь его предоставит. Добавить dependency injection для DataRoot, filesystem/launch adapters и culture/theme input в пределах будущих tests. Не запускать production `Program.Main` ради core tests.
- **Артефакты:** единственный scratch root `%TEMP%/TaskFolder-usability-qa/`; не копировать туда весь repository. Для build output сохранить существующие `bin/obj` ignores, в Git checkout проверить `git check-ignore -v` на representative path. Не скрывать docs/tests широкими patterns.
- **Проверка:** `dotnet build TaskFolder.csproj -c Release`, затем `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- baseline`. Fixtures проверяют старые launch definitions/profile roots/settings parsing; реальный DataRoot остаётся нетронутым.
- **Совместимость/rollback:** только тестовые seams; UI/JSON format на этом этапе не менять. Срок и build readiness зависят от разрешения SDK gap.

### S1. Независимая identity и надёжное сохранение

- **Результат:** одинаковые filenames в разных папках/профилях больше не разделяют metadata; ошибка записи видима.
- **Paths/symbols:** `Models/LauncherState.cs`, `ShortcutEntry.cs`, `ProfileState.cs`, `FolderState.cs`; `Services/LauncherStateStore.cs`, `StateMigrationService.cs`; facade `SettingsService.Load/Save/GetMetadata`; `ShortcutManager.LoadFromDirectory`, `RecordLaunch`, `SetCustomIcon`; `Program.OnChangeIcon` и все metadata consumers.
- **Зависимости:** S0. Зафиксировать schema и OperationResult до делегирования UI.
- **Работа:** реализовать §5.1–5.2, injectable fixtures, согласованную запись одного snapshot с Revision, last-good recovery и future-schema refusal. В рамках этого этапа перевести все metadata call sites, не оставить смешанных basename/ID accesses.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- migration`; fixtures: unique/colliding names across profiles/folders, invalid links, absent targets, corrupt JSON, disk-full/locked file, repeat startup, failure до/после replace, future schema. Reconciliation сохраняет весь legacy record и не размножает счётчики.
- **Совместимость/rollback:** legacy files и деревья не перезаписывать; пока только fixture cutover. Реальные данные — по отдельному подтверждению после preview.

### S2. Единые операции, undo и исправление targets

- **Результат:** rename/move/reorder/delete/clear/restore либо согласованно завершаются, либо дают восстановление; missing items доступны для repair.
- **Paths/symbols:** `Models/OperationResult.cs`, `Services/ShortcutOperationService.cs`, `ShortcutLaunchService.cs`, `Utilities/SafePath.cs`, `Views/RecoveryForm.cs`; `ShortcutManager.RenameShortcut/RemoveShortcut/UpdateSortOrder/LoadFromDirectory`; `ProfileService.CreateProfile/DeleteProfile`; `Program.OnMoveShortcut/OnRemoveShortcut/LaunchShortcut`, `SettingsForm.BtnClearShortcuts_Click`.
- **Зависимости:** S1. Сохранить existing launch behavior через adapter до расширения Add.
- **Работа:** journal §5.3, recoverable removal §5.4, repair, full-group reorder, path validation, rename mapping watcher. Все существующие destructive call sites направить в service; `DeleteProfile` не оставлять обходом. Вводить профильные операции только в объёме безопасного управления существующими flows.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- operations`; fault injection на каждой фазе; undo after restart; occupied restore path; copy vs move IDs; raw `.exe` не удаляется; folder с unknown files не очищается; stale Revision не пишет; duplicate events не повторяют операцию. Launch dispatch и ошибка статистики проверяются раздельно fake adapter.
- **Совместимость/rollback:** Recovery не очищать автоматически. Повреждённый journal блокирует новые mutations и предлагает recovery, но не запускает destructive repair молча.

### S3. Избранное и видимый активный профиль

- **Результат:** закреплённые приложения доступны прямо из главного меню; активный профиль очевиден.
- **Paths/symbols:** `Program.BuildTrayMenu/RefreshShortcutsList/BuildShortcutItem/RebuildProfileSubmenu`, `Views/SettingsForm.cs`, v2 fields `IsPinned/PinnedOrder/ShowRecent/ShowMostUsed`.
- **Зависимости:** S2 и утверждённый theme/localization contract для новых строк.
- **Работа:** Pin/Unpin в context menu, root pins + overflow, отдельный pin order, profile label/selector. Не создавать второй metadata record для pin. Rename/move не снимает pin; profile switch обновляет pins/label одним snapshot. Recent/Most Used toggles независимы.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- favorites`; restart, switch profile, >8 pins, pin/remove/undo, reorder, missing pinned target и keyboard activation. GUI проверяется отдельно после разрешения; существующее folder tree остаётся доступным.
- **Rollback:** UI можно откатить, additive pin fields сохранить; schema назад не понижать.

### S4. Менеджер ярлыков

- **Результат:** организация выполняется в одном окне без обязательного перехода в Проводник.
- **Paths/symbols:** `Views/ShortcutManagerForm.cs`, `Program.BuildTrayMenu`/single-instance form activation, `ShortcutOperationService.MoveItems/CopyItems/ReorderItems/CreateFolder`, `ShortcutManager` snapshots/events.
- **Зависимости:** S2–S3. Existing menu не превращать в новый файловый менеджер.
- **Работа:** дерево folders/profiles, список, multi-select, drag reorder/move, copy между profiles, создание папок, pin и aliases editor. Безопасное удаление через S2, repair/status filter. Internal drag и external file drop различаются форматом data. Keyboard/context alternatives для всех drag actions. Межпрофильная операция не переключает активный launch profile без выбора пользователя.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- organizer`; batch move preserving IDs, copy resetting counts/pin, nested/empty folders, forbidden descendant move, collision preview, watcher updates и injected failure. Разрешённый GUI walkthrough: повторные drag, cancel, focus restoration, keyboard-only и сохранение drafts при внешних изменениях.
- **Rollback:** отключить новый entry point, сохранив формат и journal; выполненные file mutations откатывать через operations, не source revert.

### S5. Универсальное добавление и каталог приложений

- **Результат:** можно добавить app/file/folder/URL/command и batch в выбранное место.
- **Paths/symbols:** `Views/AddItemDialog.cs`, `Services/InstalledAppCatalogService.cs`, `Utilities/ShellAppCatalog.cs`, `ShortcutOperationService.AddItems`, `Program.OnAddApplication`; переиспользовать `Utilities/WindowsShellLink.cs` там, где это сохраняет семантику; заменить/подключить `DragDropHelper` только для нового drop flow.
- **Зависимости:** S2/S4. Contracts Add draft и launch definition фиксируются root-owned.
- **Техническая проверка:** ограниченный spike в изолированном harness: read-only enumeration desktop shortcut, PWA и packaged Shell item; создать link в scratch и проверить parsing/identity без запуска. Exit criterion: документированный Shell identity -> persisted link -> dispatch contract для этих трёх типов. Если packaged integration не подтверждается, блокируется именно этот provider; не объявлять весь Add завершённым.
- **Работа:** async STA catalog, cancellation, icons loading, duplicate preview с arguments, file/folder dialogs, URL paste, explicit command fields и batch outcome. Никакого autorun при Add.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- adding`; в native lane `dotnet run --project tests/ShellIntegrationSmoke/ShellIntegrationSmoke.csproj -c Release -- catalog` после создания этого harness. Mock catalog не считается доказательством реального перечисления. Реальный запуск выбранных apps/PWA — только с разрешением пользователя.
- **Rollback:** existing single-file add остаётся рабочим до интеграции; новые link files сохраняют стандартную Shell совместимость.

### S6. Умный поиск и управление с клавиатуры

- **Результат:** поиск понимает aliases/слова/layout/небольшие опечатки и запускает выбранный результат.
- **Paths/symbols:** `Services/ShortcutSearchService.cs`, `Models/SearchResult.cs`, `Utilities/Windows11Menu.cs:Windows11SearchBox`, `Program.RefreshShortcutsList/LaunchFirstSearchMatch` (заменить на запуск SelectedResultId), alias editor менеджера.
- **Зависимости:** S1, S4 для редактирования aliases; не зависит от Shell app catalog.
- **Работа:** §5.6, stable ranking, breadcrumb при одинаковых именах, empty state, arrows/Enter/Escape, clear control. Сохранить focus без пересоздания всего search editor на каждый символ. Background snapshot update не запускает stale ID.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- search`; synthetic RU/EN/KO names, mixed text, spaces/punctuation, wrong layout, direct-vs-fuzzy ranking, aliases collision, zero results, changed/removed selection, timing on 1000 entries. GUI keyboard и Korean IME composition — отдельные observable checks.
- **Rollback:** можно выключить fuzzy/layout matching, сохранив aliases и basic matching; ввод не должен становиться обязательным для запуска мышью.

### S7. Экспорт и импорт конфигурации

- **Результат:** проверяемый перенос profiles/settings/assets и контролируемое объединение.
- **Paths/symbols:** `Services/BackupService.cs`, `Views/ImportPreviewForm.cs`, `ShortcutOperationService.ApplyImport`, Settings/Manager entry points; `tests/fixtures/archives/` только маленькие безопасные handcrafted fixtures.
- **Зависимости:** S1–S2, S4–S5 для complete manifest/launch definitions. Internal migration snapshot из S1 не откладывается до этого этапа.
- **Работа:** §5.7, export manifest, managed custom icon assets, import staging, quota/security validation, conflict preview, new profile default, merge/repeat import и report. Local integration preferences (startup, widget monitor/position, hotkey) preview separately: по умолчанию оставлять текущие, а не применять положение другого ПК или автоматически менять Windows registration.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- backup`; round-trip all fields/empty folders, references vs assets, corrupt/truncated archive, checksum mismatch, future schema, path traversal/ADS/absolute path, case duplicate, link payload, zip-bomb quotas, cancellation, failure during commit, repeated import. Ни один payload не запускается.
- **Rollback:** import operation undo + сохранённое предыдущее state; source revert не отменяет import. После restore сверить file/state manifest.

### S8. RU/EN и темы

- **Результат:** все собственные окна и меню согласованно локализованы и поддерживают выбранную тему.
- **Paths/symbols:** `Resources/Strings.resx`, `Resources/Strings.ru.resx`, `Services/LocalizationService.cs`, `UiThemeService.cs`; `Utilities/Windows11Menu.cs:Windows11MenuStyle/Windows11MenuRenderer/Windows11SearchBox`; Settings, Rename, IconPicker и все новые Views, строковые producers в `Program.cs`.
- **Зависимости:** resource key/theme contracts объявить в S1; финальная интеграция после появления новых UI. Resource files редактирует один владелец, чтобы избежать конфликтов.
- **Работа:** вынести UI strings/errors/plurals, ResourceManager fallback, System/RU/EN, shared palette tokens для surface/input/text/border/selection/focus. Подхватывать System preference без reset ввода. High contrast имеет приоритет над custom palette; selected state обозначается не только цветом. Native OpenFile/Folder dialogs сохраняют оформление, управляемое Windows.
- **Проверка:** `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- presentation`; resource completeness RU/EN, форматные параметры, plural cases, serialization/default migration. С разрешением GUI: light/dark/system/high contrast, disabled/focus/search/scroll/selection, длинные RU labels и 100/150/200% DPI. Проверить packaging `ru/TaskFolder.resources.dll`, отсутствующие ключи и смену темы в открытом manager.
- **Совместимость/rollback:** не использовать APIs нового TFM; недостаток публичного API конкретного native control решать существующей palette/owner drawing локально, без framework upgrade. Light fallback и сохранённый выбранный язык остаются доступны.

### S9. Блокировка widget и hover preferences

- **Результат:** пользователь управляет случайным перемещением и временем открытия menu.
- **Paths/symbols:** `Models/AppSettings.cs`, `Utilities/WidgetInteraction.cs:HoverDue/Press/Move/Release`, `Views/TaskbarWidgetForm.cs`, `Program.ApplyTaskbarSettings`, `Views/SettingsForm.cs`; `tests/WidgetPrototypeSmoke/Program.cs`.
- **Зависимости:** S1 для preferences, S8 для окончательного оформления; чистая pointer logic допускает параллельную работу по зафиксированному контракту.
- **Работа:** delay берётся из settings, clamp 100–1500 ms, default 300; lock запрещает start drag и position commit, сохраняя обычный click/hover/context action. Изменение delay/lock во время press отменяет drag предсказуемо. Не менять folder hover 200 ms и taskbar hosting/recovery алгоритм.
- **Проверка:** core pointer cases `dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- widget`; boundary 99/100/299/300/1499/1500, hover disabled, menu already open, leave/reenter, lock click vs drag, monitor/DPI coordinate handling, restore settings. После просмотра текущих тестов выполнить native/default smoke в разрешённой lane. Live taskbar, Start, monitor change и outside click требуют отдельной interactive проверки.
- **Rollback:** preferences additive; old default 300/false позволяет восстановить прежнее поведение без сброса координат.

### S10. Интеграция, документация и release candidate

- **Результат:** единый проверенный candidate и конкретный отчёт по доказанным/непроверенным сценариям.
- **Paths/symbols:** `README.md`, `docs/usability-upgrade.md`, `TaskFolder.csproj`, `TaskFolder.iss`, `BuildInstaller.ps1`, все затронутые tests; orchestration-only финальные изменения в `Program.cs` и contracts.
- **Зависимости:** S0–S9. Версию candidate выбрать после интеграции, не выдавать её за уже опубликованную.
- **Работа:** regression matrix §9, migration/rollback runbook, синхронизация README с реальным persistence, документация features и export limits. Проверить folder publish и satellite resources без single-file extraction. Не менять Defender/startup/infrastructure. Review final diff либо, при отсутствии Git, explicit source manifest и hashes; проверить неожиданные files/artifacts.
- **Проверка:** команды §9, permission-aware GUI/Shell walkthrough, self-contained folder publish при доступном SDK. Installer compile разрешён как создание локального артефакта после проверки скрипта; install/restart реального app и публикация — отдельные действия с разрешением.
- **Rollback:** не выпускать candidate с проваленной integrity/recovery проверкой. Недоступная manual platform остаётся явным gap с точным следующим сценарием. Никаких live destructive Power actions ради smoke.

## 9. Проверка и traceability

### Команды

Команды заданы для будущего исполнителя; **в ходе планирования они не выполнялись**. Сначала S0 и просмотр возможных side effects каждого harness.

```powershell
dotnet build TaskFolder.csproj -c Release
dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- all
dotnet run --project tests/PowerMenuSmoke/PowerMenuSmoke.csproj -c Release
dotnet run --project tests/TaskbarPrototypeSmoke/TaskbarPrototypeSmoke.csproj -c Release
dotnet run --project tests/WindowsIntegrationSmoke/WindowsIntegrationSmoke.csproj -c Release
```

`CoreSmoke` должен использовать отдельный scratch DataRoot и fake launch/native power adapters. `PowerMenuSmoke` сейчас проверяет dispatch через fake actions и English labels: сохранить этот test contract с явной `en` culture и добавить RU test. `WindowsIntegrationSmoke` пишет только в temporary HKCU key/Programs folder; повторно проверить изоляцию перед запуском.

`WidgetPrototypeSmoke` содержит native hidden-window, render и live modes. Default не считать чистым unit test. Его и новый `ShellIntegrationSmoke -- catalog` запускать в native lane после проверки side effects и необходимых разрешений. `--live`, `--probe-running`, `--render`, управление GUI, запуск реальных apps/PWA, изменение установленной конфигурации и установка candidate не входят автоматически в разрешение на написание исходников.

Существующие prototype tests важны как regression evidence, но проверяют в том числе альтернативные/исторические пути: не выдавать их PASS за доказательство работы production taskbar widget.

### Матрица покрытия

| Требование / риск | Этапы | Verification | Сигнал в установленном candidate |
| --- | --- | --- | --- |
| R01 pins | S1–S3 | pin/order/profile/restart/undo tests | Pin виден и сохраняется после restart |
| R02 manager | S2/S4 | batch move/copy/reorder + GUI keyboard/drop | Menu и manager показывают один результат |
| R03 search | S6 | ranking, aliases, layout, KO/IME, stale ID, perf | Выбранный результат запускается, пустой поиск не запускает старый |
| R04 add | S5 | file/folder/URL/command fixtures + Shell catalog | Desktop/PWA/packaged links сохраняют launch identity |
| R05 recovery/repair | S1/S2/S4 | crash/undo/path conflict/raw EXE/missing/unavailable | Элемент восстановлен со всеми metadata, target не удалён |
| R06 backup | S7 | round-trip, malicious archives, repeated import | Preview и итог совпадают, настройки ОС не перезаписаны |
| R07/R08 widget | S9 | pure state + native/live lanes | Lock не двигает icon, hover delay применён один раз |
| R09 profile | S3/S4 | switch/label/pins/content consistency | Активный профиль виден и соответствует запускам |
| R10 locale | S8/S10 | key coverage, format, packaging, RU DPI | Нет пропавших строк/обрезанных labels |
| R11 theme | S8 | palette + visual high contrast/DPI | Readable focus/disabled/selected states |
| R12 identity/save | S1/S2 | collision fixture, replace faults, journal recovery | Нет loss/duplication, ошибка записи видима |
| PWA/shell invariants | S2/S5/S10 | preserved link bytes + native link parsing + allowed real launch | Правильное dedicated PWA окно, arguments/cwd сохранены |
| Regression of taskbar/startup/power | S9/S10 | existing tests + allowed GUI checks | Widget survives existing recovery flows; no unwanted OS mutations |

Fixtures включают: два профиля с одинаковыми filenames, вложенные/пустые папки, кириллицу/корейский/пробелы, два PWA одного browser с разными arguments, raw executable placeholder, external file/folder, недоступный drive, malformed `.url`, stale metadata, external custom icon и existing unsaved draft.

Scratch screenshots, logs и archives — только в едином внешнем task output root. В repository попадают лишь small canonical fixtures/tests, intended docs/source. По каждому milestone хранить concise result: команды/exit codes, source fingerprint, fixture scenario, observations, gaps. Не сохранять личные targets/URLs/arguments и secrets в diagnostics без явного выбора пользователя.

## 10. Внедрение и эксплуатация

Практическая последовательность candidate milestones:

1. **A — надёжная основа:** S0–S2. Проверить migration/recovery до adoption UI. Только fixtures и согласованный test data root.
2. **B — ежедневное удобство:** S3–S4. Избранное, активный профиль, manager.
3. **C — быстрый ввод и поиск:** S5–S6.
4. **D — перенос и оформление:** S7–S9; независимые части допускают parallel execution по §11.
5. **E — единый candidate:** S10, final acceptance. Отдельная публикация только по запросу.

До разрешённого live cutover: остановить старый процесс, проверить реальный data root/portable mode, preview migration и scope snapshot. Не создавать автоматическую существенную копию без согласия. Первый run новой версии не должен сам менять startup/Windows registration или запускать импортируемые элементы.

Первая pilot-сессия после разрешённой установки: migration/restart, app/PWA launch, pin, manager move+undo, search, export в выбранный путь, import в новый test profile, widget lock/hover, RU/theme. Наблюдение — одна полная рабочая сессия и restart; это плановая acceptance window, а не заявленная гарантия стабильности.

Abort criteria: любое расхождение migration inventory без объяснения, потеря IDs/metadata, невосстановимый journal, запись вне подтверждённого root, запуск во время Add/import, удаление target, нарушение PWA arguments или зависание UI на сетевом пути. При таком результате прекратить новые mutations, сохранить evidence и использовать recovery; не повторять небезопасный шаг циклом.

Нет cloud rollout dashboard: local success/error result, Revision, unresolved migration count и pending journal state достаточно для этого single-user scope. Опциональный diagnostic export содержит только выбранные пользователем данные и коды ошибок. Recovery/assets показывают занятое место; никаких скрытых auto-purge jobs.

Сохранить runbook `docs/usability-upgrade.md`: поддерживаемые schemas, preview, snapshot composition, pending-operation recovery, import limits, rollback до/после file mutations, отличия конфигурации от установленных программ и сценарии, требующие отдельного разрешения.

## 11. Владение реализацией и условное делегирование

Этот раздел — контракт реализации. **При написании плана subagents не запускались.** По умолчанию один агент выполняет ограниченные этапы последовательно, сохраняя prerequisites и gates каждого этапа. Делегирование допустимо только при разрешении текущей среды и пользователя, когда независимый результат или необходимое независимое review оправдывает координацию; указать конкретную пользу. Вложенная оркестрация по умолчанию не нужна. Недоступность subagents не блокирует последовательную работу, кроме явно заданного пользователем или обоснованного критерием приёмки требования независимого исполнения.

Orchestrator владеет baseline, schema/migration, `Program.cs`, общими models, `SettingsService`, интеграцией, разрешениями и финальной приёмкой. При работе одним агентом Orchestrator означает его основную роль; роли subagents в таблице ниже — возможные пакеты делегирования, а не обязательные отдельные запуски. Shared paths не редактируются несколькими агентами одновременно. Перед каждой задачей выдаются allowed/protected paths, конкретные prerequisites, expected artifact и команда проверки.

| Задача | Этапы / prerequisites | Владелец и allowed paths | Protected/shared paths | Artifact / проверка / интеграция |
| --- | --- | --- | --- | --- |
| Baseline и contracts | S0/S1 | Orchestrator: project, state models/store/migration, test root | Все unrelated source/artifacts | Schema + migration fixtures; review до остальных |
| Recoverable operations | S2 после schema gate | Один operations subagent: `ShortcutOperationService`, `SafePath`, operation tests | `Program.cs`, profile/settings adapters и shared models — orchestrator | Diff + failure matrix; orchestrator интегрирует callers |
| Favorites/menu wiring | S3 после S2 | Orchestrator: `Program.cs`, Settings integration | Остальные View workstreams | Pin/profile regression evidence |
| Manager UI | S4 после S2/S3 | Один UI subagent: `ShortcutManagerForm`, его локальные helpers/tests | `Program.cs`, shared services/models/resources | Diff + сценарии; root подключает entry point |
| Add и Shell catalog | S5 после Add contract | Catalog subagent: `AddItemDialog`, catalog service, `ShellAppCatalog`, `ShellIntegrationSmoke` | `WindowsShellLink`/`Program`/resources edits через root | Provider evidence; real GUI/launch отдельно |
| Search | S6 после snapshot/alias contract | Search subagent: `ShortcutSearchService`, `SearchResult`, search fixtures/tests | `Program.cs`, `Windows11Menu.cs` через root/UI owner | Ranking/perf evidence; root интегрирует selection |
| Archive | S7 после schema/operations gate | Backup subagent: `BackupService`, `ImportPreviewForm`, archive tests | Shared state/Program/Settings/resources | Round-trip + abuse fixtures; root интегрирует commands |
| Localization/theme | S8 после UI string inventory | Один presentation subagent: resources, localization/theme service, renderer; Views только при эксклюзивном ownership | Новые Views нельзя менять одновременно с их владельцем | Resource/visual report; final integration serial |
| Widget preferences | S9 после settings contract | Widget subagent: `WidgetInteraction`, `TaskbarWidgetForm`, widget tests | Hosting/recovery algo, `Program`/Settings/models через root | Boundary/native evidence; root settings integration |
| Final candidate | S10 после всех gates | Orchestrator | Unrelated user files и установленная конфигурация | Final source review, regression report, docs; release отдельно |

Parallel-safe после фиксации контрактов: pure search engine, archive validation/serialization и pointer logic widget, поскольку их allowed paths не пересекаются. Manager/Add/presentation overlap по Views/resources сериализовать. Не писать по отдельной копии всего source tree для каждого этапа. При делегировании diff и evidence каждого пакета проверяются до снятия следующего gate. В последовательном режиме сохраняются те же проверки интеграции, безопасности, регрессий и приёмки; уже успешные проверки неизменившихся исходников повторно не запускаются без нового основания.

## 12. Критерии приёмки

- [ ] Все R01–R12 имеют работающий UI flow и evidence из §9; сцены/рабочие наборы отсутствуют.
- [ ] Профили и одноимённые shortcuts независимы; legacy records/история не потеряны и не задвоены. Неоднозначные records сохранены и видны для разрешения.
- [ ] Rename/move сохраняют ID и metadata; copy получает новый ID и отдельную статистику; полный порядок группы сохраняется после restart.
- [ ] Pins доступны напрямую, корректны для активного профиля; Recent/Most Used управляются независимо; profile виден в главном меню.
- [ ] Manager поддерживает папки, batch, drag и keyboard equivalents; изменения согласованно отражаются в меню без потери drafts.
- [ ] Add принимает все заявленные типы, выбирает destination и обрабатывает batch/duplicates; catalog включает проверенные desktop/PWA/packaged providers.
- [ ] Search выполняет правила ranking/layout/aliases/fuzzy; Enter запускает selected ID, zero results не запускает старый элемент; Korean IME не вызывает преждевременный запуск.
- [ ] Удаление и очистка не удаляют target/raw EXE; restore после restart возвращает bytes и metadata без overwrite нового файла. Missing/unavailable items доступны для repair.
- [ ] Export/import переносит заявленные данные; предупреждает о внешних targets/assets; preview/cancel/idempotency/conflicts/quotas и malicious archive tests подтверждены.
- [ ] RU/EN и System/Light/Dark охватывают все собственные surfaces; high contrast, DPI и keyboard focus читаемы; satellite resources входят в publish.
- [ ] Widget lock/hover settings работают после restart; существующие координаты, folder hover, taskbar fallback и recovery сохранены.
- [ ] Fault tests подтверждают last-good JSON и journal recovery; user-visible Success не появляется при неуспешной записи.
- [ ] Build и существующие применимые smoke tests выполнены в доступном toolchain; native/GUI/Windows 10/11 evidence указаны раздельно, без подмены mocks/static checks.
- [ ] Получены согласованные performance measurements по reference fixture; невыполненный бюджет отражён как открытая работа.
- [ ] README/runbook соответствуют коду; intended files отделены от ignored scratch; unrelated files не удалены/не скрыты. Установленная версия и пользовательские данные менялись только по отдельному разрешению.

## 13. Блокеры и точки повторной проверки

1. **SDK gate до сборки:** системный `dotnet` сейчас не видит SDK. Найти уже установленный подходящий toolchain; если его нет, запросить конкретную установку отдельно. Это не блокер для чтения/проектирования, но блокер build evidence и завершения реализации.
2. **Baseline gate:** source folder без Git history. До edits повторно сверить hashes и сохранить explicit intended-change manifest. Если нужен branch/commit/release workflow, пользователь должен указать/разрешить соответствующий checkout и действия; не угадывать remote.
3. **Runtime gates по мере этапов:** packaged Shell-link spike, GUI/DPI/IME/taskbar и реальные launches пока не выполнены. Эти проверки нужны для приёмки соответствующих функций; разрешение на управление GUI, запуск apps и установку запросить непосредственно перед конкретной проверкой. Они не мешают независимой реализации pure logic.

Нерешённых продуктовых вопросов, требующих остановить подготовку исходников после запроса на реализацию, нет. Принятые defaults и все исключения перечислены в этом документе.
