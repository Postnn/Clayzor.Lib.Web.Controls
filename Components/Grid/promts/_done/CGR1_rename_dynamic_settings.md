# CGR1 — переименование `ClayGridDynamicOptions` → `ClayGridDynamicSettings`

> Промт для агента. Стиль исполнения — по корневому `AGENTS.md`:
> **Think Before Coding → Simplicity First → Surgical Changes → Goal-Driven Execution.**
> Весь пользовательский текст — **на русском**. Git (add/commit/push) — **только по прямому
> указанию**. Документацию правь **только** в файлах, перечисленных в §4 (на них указание дано);
> прочую документацию не трогай.
>
> Один заход = один коммит. Требует выполненной серии **CGO** (в частности шага C2, где в
> `AGENTS.md` появился раздел про Options). Если серия CGO не выполнена — **ОСТАНОВИСЬ и спроси**.

---

## Задача

Переименовать класс настроек динамического режима грида:

```
ClayGridDynamicOptions  →  ClayGridDynamicSettings
```

Причина — конвенция имён, зафиксированная по итогам серии CGO:

- **`*Settings`** — настройки уровня приложения: байндятся из `appsettings.json` / `web.config`,
  живут в DI, одни на приложение (`ClayAppSettings`, теперь `ClayGridDynamicSettings`);
- **`*Options`** — настройки одного экземпляра компонента на странице, задаются страницей
  (`ClayGridOptions`, `ClayTreeOptions`).

До переименования два класса с почти одинаковыми именами — `ClayGridOptions` и
`ClayGridDynamicOptions` — означали принципиально разные вещи, и в xml-doc приходилось держать
абзац «не путать с…».

**Это чистое переименование. Ни одного изменения поведения, ни одного нового или удалённого
свойства, ни одной правки логики.**

---

## Контекст: где что лежит

- Класс: `src/Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/ClayGridDynamicOptions.cs`,
  namespace `Clayzor.Lib.Web.Controls.Components.Grid.Dynamic`.
  **Он в Controls, а не в Entities** — если в старых промтах серии CGO указан путь в
  `Clayzor.Lib.Entities`, это ошибка того промта, ориентируйся на фактическое расположение.
- `ClayGridSchemaMap` (свойство `Schema` этого класса) живёт в
  `Clayzor.Lib.Entities.DynamicGrid` — **другой класс, не переименовывается**.
- Направление зависимостей: `Controls → Entities`. Значит, из `Clayzor.Lib.Entities`
  ссылок на переименовываемый класс быть не может физически; в этом проекте возможны только
  упоминания в документации.

## Прочитать перед началом

- `Components/Grid/Dynamic/ClayGridDynamicOptions.cs` — целиком, включая тексты `Validate()`;
- `Components/Grid/Dynamic/ServiceCollectionExtensions.cs` — целиком (короткий);
- `Components/Grid/ClayGrid.Dynamic.cs` — инжект опций и все методы, принимающие их параметром
  (`ResolveClientId`, `RestoreDynamicState`, `SaveDynamicState`, `ResolveDynamicGridId`,
  `InitDynamicMode` — проверь фактический список);
- `tests/Clayzor.Lib.Web.Controls.Tests/OptionsBindingTests.cs`;
- `src/Clayzor.Lib.Web.Controls/AGENTS.md` — раздел про динамический режим и раздел
  «Настройки компонентов (Options)», добавленный шагом CGO C2.

---

## Зафиксированные решения — что НЕ переименовывается

Прочитай до кода. Соблазн «раз уж переименовываем — приведём всё к одному виду» здесь ломает
работающие приложения.

| Оставить как есть | Почему |
|---|---|
| секция конфигурации **`"ClayGrid:Dynamic"`** | к имени класса не привязана; её переименование ломает `appsettings.json` и `web.config` **в обоих приложениях**, включая продакшен-конфиги, которых в репозитории нет |
| метод **`AddClayGridDynamic()`** | назван по фиче, а не по классу; менять — ломать `Program.cs` потребителей без выигрыша |
| **`ClayGridSchemaMap`** и его вложенные классы | это маппинг имён колонок, к паре Settings/Options не относится, живёт в другом проекте |
| **`ClayGridUserParamsData`, `ClayGridDefinition`, `ClayColumnDefinition`, `DynamicSql`** | классы данных, не настройки |
| имена **свойств** класса (`ConnectionStringName`, `SettingsTable`, `GridIdQueryParam`, префиксы) | привязаны к ключам конфигурации — переименование свойства молча обнуляет настройку у потребителя |
| имя файла тестов **`OptionsBindingTests.cs`** | он проверяет байнд `IOptions<>`-настроек и дефолты `ClayGridSchemaMap`; имя не стало неверным. Если считаешь нужным переименовать — **предложи в отчёте**, сам не делай |
| файлы в **`Components/Grid/promts/_done/`** | исторический архив выполненных задач; правка задним числом уничтожает возможность понять, что и почему делалось (см. §«Не делай») |

Переименовывается: **класс, его файл, внутренний валидатор** и все ссылки на них в коде,
xml-doc и актуальной документации.

---

## Пошагово

### 1. Класс и файл

- Переименуй файл: `ClayGridDynamicOptions.cs` → `ClayGridDynamicSettings.cs`.
  **Именно переименование** (`git mv`), а не «создать новый + удалить старый» — иначе теряется
  история файла в git.
- Класс `ClayGridDynamicOptions` → `ClayGridDynamicSettings`.
- Xml-doc класса: `Настройки динамического режима ClayGrid, связываемые из секции
  "ClayGrid:Dynamic"` — оставить по смыслу, дописать одной фразой, что это настройки уровня
  приложения (в отличие от `ClayGridOptions` — настроек экземпляра грида на странице).
- **Тексты исключений в `Validate()`** содержат имя класса строковым литералом:
  `"ClayGridDynamicOptions.ConnectionStringName пусто"` и ещё три подобных. Обнови все.
  См. ловушку в §5 — это единственное место, которое не поймает ни компилятор, ни тесты.

### 2. DI

`Components/Grid/Dynamic/ServiceCollectionExtensions.cs`:

- `services.Configure<ClayGridDynamicSettings>(config.GetSection(section));`
- `services.AddSingleton<IValidateOptions<ClayGridDynamicSettings>, ValidateClayGridDynamicSettings>();`
- внутренний класс `ValidateClayGridDynamicOptions` → `ValidateClayGridDynamicSettings`
  (остаётся `internal sealed`, интерфейс `IValidateOptions<T>` — **не** меняется: это тип
  из `Microsoft.Extensions.Options`, и то, что настройки называются `*Settings`, ему безразлично);
- xml-doc метода `AddClayGridDynamic` и класса-валидатора — обновить ссылки `<see cref=...>`;
- **сигнатуру `AddClayGridDynamic` и значение по умолчанию `section` не трогать.**

### 3. Потребители в коде

`Components/Grid/ClayGrid.Dynamic.cs` и, если найдутся, остальные `ClayGrid.Dynamic.*.cs`:

- инжект `[Inject] IOptions<ClayGridDynamicSettings> DynamicOpts` (имя поля `DynamicOpts`
  **оставить** — переименование локального поля к задаче не относится и раздувает диф);
- параметры методов `ClayGridDynamicOptions opt` → `ClayGridDynamicSettings opt` (имя параметра
  `opt` оставить);
- локальные переменные не переименовывать.

Найди их не глазами, а так:

```
grep -rn "ClayGridDynamicOptions" src/ tests/
```

### 4. Тесты и документация

- `tests/Clayzor.Lib.Web.Controls.Tests/OptionsBindingTests.cs` — все `new ClayGridDynamicOptions`
  и упоминания в xml-doc тестов. Сами утверждения (`Assert`) **не менять**: они проверяют
  имена полей и значения, а не имя класса.
- `src/Clayzor.Lib.Web.Controls/AGENTS.md`:
  - строка про `ServiceCollectionExtensions.AddClayGridDynamic()` в таблице динамического режима;
  - раздел «Настройки компонентов (Options)» (создан CGO C2): абзац «не путать
    `ClayGridOptions` и `ClayGridDynamicOptions`» **заменить на правило именования** —
    `*Settings` = уровень приложения (из конфигурации, в DI), `*Options` = экземпляр компонента
    на странице; примеры: `ClayAppSettings`, `ClayGridDynamicSettings` / `ClayGridOptions`,
    `ClayTreeOptions`. Правило короче и полезнее, чем предупреждение о путанице;
  - если в разделе выполненных шагов ведётся история — одна строка про CGR1.
- `src/Clayzor.Lib.Web.Controls/README.md` — раздел «Динамический грид» (фраза «Настройки
  задаются через `ClayGridDynamicOptions` (секция `"ClayGrid:Dynamic"`)») и раздел
  «Подключение», если класс там назван.
- `src/Clayzor.Lib.Entities/AGENTS.md` и `docs/clay-grid.md` — **только если** grep нашёл
  упоминания. Не искать «что бы ещё дописать».

---

## 5. Ловушка — прочитай обязательно

**Ни компилятор, ни тесты не докажут, что переименование выполнено полностью.**

Тесты проверяют вхождение **имени поля** в текст исключения:

```csharp
Assert.Contains("ConnectionStringName", ex.Message);
```

Значит, если в `Validate()` останется литерал `"ClayGridDynamicOptions.ConnectionStringName
пусто"`, сборка будет зелёной, тесты — зелёными, а пользователь при пустой настройке получит
сообщение про класс, которого в решении больше нет. То же касается упоминаний в xml-doc,
markdown-документации и в текстах любых других сообщений.

Отсюда единственный приемлемый критерий приёмки: **`grep -rn "ClayGridDynamicOptions" src/ tests/`
возвращает ноль попаданий** (кроме архива `promts/_done/`, см. ниже). Не «сборка зелёная».

Второе: делай переименование средствами IDE (Rename / F2) — оно правит и `<see cref="..."/>`
в xml-doc. Но после этого всё равно прогони grep: строковые литералы и markdown IDE не видит.

---

## Не делай

- **Не правь файлы в `Components/Grid/promts/_done/`** и вообще ничего в архивах выполненных
  задач. Там старое имя класса — это факт истории, а не ошибка. Упоминания старого имени в
  архиве — ожидаемые попадания grep'а, их надо исключить из проверки, а не «исправить».
- Не переименовывай секцию `"ClayGrid:Dynamic"`, метод `AddClayGridDynamic()`, свойства класса,
  `ClayGridSchemaMap`, классы данных — см. таблицу решений.
- Не трогай `appsettings.json`, `web.config`, `Program.cs` приложений: имя типа там не
  фигурирует. Если фигурирует — **стоп, это находка, спроси**.
- Не меняй `ClayGridOptions` и `ClayTreeOptions`: они названы правильно, решение заказчика
  зафиксировано.
- Не добавляй и не удаляй свойства, не меняй дефолты, не трогай `Validate()` по существу
  (только текст сообщений), не меняй порядок членов класса.
- Не переименовывай поле `DynamicOpts`, параметры `opt`, локальные переменные — диф должен
  состоять из замены имени типа и его файла, и ничего больше.
- Не переименовывай `OptionsBindingTests.cs` (предложение — в отчёт).
- Не правь документацию за пределами §4.
- Не делай git-коммит без прямого указания. Файл переименуй через `git mv`.

---

## Проверка

**Механическая — здесь она главная**

- `grep -rn "ClayGridDynamicOptions" src/ tests/` → попадания **только** в
  `src/Clayzor.Lib.Web.Controls/Components/Grid/promts/_done/` (архив). Вывод команды приложи
  к отчёту целиком;
- `grep -rn "ClayGridDynamicSettings" src/ tests/` → класс, валидатор, DI-регистрация, инжект,
  параметры методов, тесты, `AGENTS.md`, `README.md` — и больше ничего лишнего;
- `git status` / `git diff --stat`: файл показан как **переименованный** (`R`), а не как
  «удалён + добавлен»;
- `grep -rn "ClayGrid:Dynamic" src/` → значение секции не изменилось (DI-расширение +
  `appsettings.json` обоих приложений);
- `dotnet build Clayzor.sln` — зелёный, без новых warning'ов;
- `dotnet test tests\Clayzor.Lib.Web.Controls.Tests` — зелёный.

**Ручная — проверка того, что тесты доказать не могут**

- `Kesco.App.Web.Inventory`, `/?id=140`: грид открывается и грузит данные сразу; сохранение и
  восстановление состояния (колонки, сортировка, группировка, размер страницы, фильтр) работает
  — уйти со страницы и вернуться;
- `/` без `?id=` → сообщение «Не указан код запроса…» (проверка, что настройки доехали до
  `ResolveDynamicGridId`);
- **проверка обновлённых литералов:** временно очистить `ClayGrid:Dynamic:ConnectionStringName`
  в `appsettings.json` приложения → при обращении к настройкам сообщение об ошибке содержит
  **`ClayGridDynamicSettings.ConnectionStringName`**, а не старое имя. **Вернуть настройку
  обратно.** Это единственный способ убедиться, что §5 выполнен;
- `Clayzor.App.Web.MedicalTests`, `/medical-tests`: статический грид работает как до задачи
  (динамических настроек не касается, но код грида общий).

**Отчёт:** переименованные сущности; сколько строковых литералов пришлось поправить в
`Validate()`; какие файлы документации задеты; предложение по `OptionsBindingTests.cs`;
любые найденные упоминания старого имени в неожиданных местах (конфиги, JS, CSS, скрипты БД).
