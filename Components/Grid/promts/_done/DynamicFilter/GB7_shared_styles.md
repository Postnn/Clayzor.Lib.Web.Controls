> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **STYLE_RULES.md** целиком. Требует выполненных **GB1**, **GB2**, **GB6**. Выполнять **до GB3–GB5** — иначе те же правки придётся делать дважды. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB7 — общие стили: один источник вместо двух копий app.css

Это **рефакторинг, а не багфикс**: поведение и внешний вид обязаны остаться байт-в-байт теми же.
Ценность — в том, что после него правки стилей грида делаются один раз.

Прочитать перед началом: `STYLE_RULES.md` — целиком (§1 «где живёт стиль», §2 запреты,
§4 таблица паттернов, §5 белый список, §6 enforcement); `src/Clayzor.App.Web.MedicalTests/AGENTS.md`
— разделы Typography & Fonts, Style enforcement, Architecture (Variant A);
`src/Clayzor.App.Web.MedicalTests/wwwroot/css/app.css` и
`src/Kesco.App.Web.Inventory/wwwroot/css/app.css` — **обе целиком**, и глазами по `diff`;
`src/Clayzor.App.Web.MedicalTests/Components/App.razor` и
`src/Kesco.App.Web.Inventory/Components/App.razor` — как подключаются `_content/…` ресурсы RCL;
`src/Clayzor.Lib.Web.Controls/Clayzor.Lib.Web.Controls.csproj` (Sdk.Razor → `wwwroot` едет как
static web assets); `build/StyleGuard.targets` — что именно сканируется и какой белый список.

## Дефект

Стили компонентов из RCL `Clayzor.Lib.Web.Controls` (грид, треи, чипы, диалоги, кнопки тулбара,
спиннер печати) живут в двух копиях `app.css` — по одной на приложение. Компонент в библиотеке
один, стили к нему — два файла. Каждая правка делается дважды, забытая половина проявляется
только в том приложении, куда не заглянули.

Копии уже разъехались (`diff` — ~45 строк):

| Есть в Kesco, нет в MedicalTests | Есть в MedicalTests, нет в Kesco |
|---|---|
| `.grouping-tray-add-btn`, `.grouping-tray-add-btn:hover` | `.clay-grid-error` |
| `.chip-group-badge`, `.clay-group-toggle` | — |
| `.grouping-tray-add-btn` в `@media print` (`.clay-grid-printing`) | — |
| `.clay-column-settings-header { margin-bottom: -4px }` | то же правило, но `2px` |

То есть в `MedicalTests` кнопка «+» в трее группировки и бейдж уровня группировки **сейчас
не стилизованы вообще** — просто этого никто не заметил.

`STYLE_RULES.md` §1 при этом утверждает, что стиль живёт «ТОЛЬКО в
`Clayzor.App.Web.MedicalTests/wwwroot/css/app.css`». Для одного приложения правило работало;
с появлением `Kesco.App.Web.Inventory` оно перестало описывать реальность и стало источником
дублирования. Правило надо не обойти, а **переформулировать**: общий стиль общих компонентов
живёт в RCL, стиль конкретного приложения — в его `app.css`.

## Изменить/создать

### 1. Общий файл в RCL

Создать `src/Clayzor.Lib.Web.Controls/wwwroot/css/clay.css` (каталог `wwwroot/js` в RCL уже
есть и раздаётся как `_content/Clayzor.Lib.Web.Controls/js/…` — с `css` будет ровно так же,
дополнительных настроек csproj не требуется; если окажется, что требуется — сделай минимально
необходимое и напиши в отчёте).

Содержимое = **объединение** двух `app.css`:

- **База — копия Kesco** (она новее: `.grouping-tray-add-btn`, `.chip-group-badge`,
  `.clay-group-toggle`, print-правило с `.grouping-tray-add-btn`);
- **добавить из MedicalTests**: `.clay-grid-error` (перенести как есть; по grep-у в `.razor`/`.cs`
  на неё ссылок нет — **не удалять**, это pre-existing dead code, только строка в отчёте);
- расхождение `margin-bottom: -4px` / `2px` в `.clay-column-settings-header` → взять
  **Kesco-вариант (`-4px`)**: приложения должны выглядеть одинаково, а это правило всё равно
  умрёт в GB4;
- **не переносить** в общий файл блок `/* ── Dense form (MedicalTestEditDialog) ── */` — он про
  конкретный диалог конкретного приложения.

Проверка полноты: `clay.css` обязан содержать `:root` с `--clay-*`/`--lh-*` алиасами, `html`/`body`
типографику, все override-ы MudBlazor (инпуты, лейблы, legend), стили грида/треев/чипов/диалогов,
блок `@media print`. Это единственный «скелет» — приложение без своего `app.css` должно
выглядеть корректно.

### 2. `app.css` приложений — только своё

- `src/Clayzor.App.Web.MedicalTests/wwwroot/css/app.css` — оставить **только** блок
  `/* ── Dense form (MedicalTestEditDialog) ── */` и шапку-комментарий. Всё остальное удалить
  (оно уехало в `clay.css`).
- `src/Kesco.App.Web.Inventory/wwwroot/css/app.css` — своего у него нет; оставить файл пустым
  с комментарием-заглушкой:
  ```css
  /* Стили этого приложения. Общий стиль компонентов Clayzor — в
     _content/Clayzor.Lib.Web.Controls/css/clay.css (см. STYLE_RULES.md §1). */
  ```
  Файл **не удалять** и ссылку на него из `App.razor` **не убирать**: пустой файл — штатное
  место для будущих правок конкретного приложения, а его отсутствие → 404 в консоли.

### 3. `App.razor` обоих приложений — порядок подключения

```html
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<link href="_content/MudBlazor.Extensions/mudBlazorExtensions.min.css" rel="stylesheet" />
<link href="_content/Clayzor.Lib.Web.Controls/css/clay.css" rel="stylesheet" />
<link href="css/app.css" rel="stylesheet" />
```

Строго в этом порядке: `clay.css` после MudBlazor (перебивает его правила, как раньше делал
`app.css`) и до `app.css` (чтобы приложение могло перебить общий стиль при равной
специфичности). Ничего другого в `App.razor` не менять.

### 4. `STYLE_RULES.md` — переформулировать §1 и §5

Это **прямое указание** на правку документации (см. `/AGENTS.md`: только по указанию).

- §1: стиль живёт в **`Clayzor.Lib.Web.Controls/wwwroot/css/clay.css`** (общие компоненты),
  в `<App>/wwwroot/css/app.css` (только специфика приложения) и в
  `Clayzor.Lib.Web.Controls/Themes/ClayTheme.cs` (палитра). Явно записать правило выбора:
  **стилизуешь компонент из RCL → `clay.css`; стилизуешь страницу/диалог конкретного
  приложения → его `app.css`.** И запрет: не копировать правила между приложениями.
- §4 (таблица паттернов): в колонке «где» вместо `app.css` — `clay.css` для общих паттернов.
- §5/§6 (белый список StyleGuard): добавить `clay.css`, если список файлов там перечислен
  поимённо. StyleGuard сканирует `.razor`/`.cs`, css-файлы он не читает — но убедись сам,
  а не по памяти.

### 5. AGENTS.md

- `src/Clayzor.App.Web.MedicalTests/AGENTS.md` — в разделах Style enforcement / Architecture
  заменить «стиль только в app.css этого приложения» на новую формулировку из §1.
- `/AGENTS.md` — в разделе про `App.razor` (список подключаемых скриптов RCL) добавить строку
  про `clay.css`.
- `src/Clayzor.Lib.Web.Controls/AGENTS.md` — одна строка: где живёт `clay.css` и что правится
  он, а не копии в приложениях.

Другую документацию не трогать.

## Не делай

- **Не меняй ни одного правила по существу.** Ни селекторов, ни значений, ни порядка внутри
  файла. Единственные разрешённые расхождения с исходником — три штуки, перечисленные в п. 1
  (добавленный `.clay-grid-error`, выбранный `-4px`, оставленный в приложении Dense form).
  Всё прочее — механический перенос. `diff` между старым Kesco-`app.css` и новым `clay.css`
  должен читаться за минуту.
- Не переводи стили на CSS isolation (`.razor.css`) и на scoped-стили — это другая архитектура,
  сломает `!important`-override-ы MudBlazor и глобальные `@media print`.
- Не дроби `clay.css` на несколько файлов (grid.css, dialogs.css, …) — сейчас это лишняя
  сущность; один файл вместо двух копий уже решает задачу.
- Не удаляй `app.css` из приложений и ссылки на них из `App.razor`.
- Не удаляй pre-existing dead code (`.clay-grid-error` и всё, что покажется неиспользуемым) —
  находки в отчёт (`/AGENTS.md`, Surgical Changes).
- Не трогай `ClayTheme.cs`, `ClayColors.cs`, `ClayGridPrintStyles.cs` и генераторы
  печати/Excel — у них свой автономный HTML (STYLE_RULES §5).
- Не переименовывай классы и не чисти legacy-алиасы `--lh-*` — отдельная задача.

## Проверка

- `dotnet build Clayzor.sln` зелёный; `dotnet test tests\Clayzor.Lib.Web.Controls.Tests` зелёный;
- в DevTools → Network: `_content/Clayzor.Lib.Web.Controls/css/clay.css` отдаётся 200,
  `css/app.css` — 200 (пустой у Kesco);
- **Kesco `?id=140`**: грид, треи группировки и фильтрации, чипы, бейджи, кнопка «+» в трее,
  диалоги (настройка колонок, фильтр, подтверждение), тулбар, пагинация, спиннер печати —
  визуально идентичны состоянию до рефакторинга (сравнить со скриншотами, снятыми ДО);
- **`/medical-tests`**: то же самое **плюс** проявились ранее отсутствовавшие стили —
  кнопка «+» в трее группировки (`.grouping-tray-add-btn`) и золотой бейдж уровня группировки
  (`.chip-group-badge`) теперь выглядят как в Kesco. Это ожидаемое следствие слияния, не регресс;
  зафиксируй в отчёте;
- `MedicalTestEditDialog` → плотная форма выглядит как раньше (Dense form остался в app.css
  приложения и грузится ПОСЛЕ `clay.css`);
- тёмная тема (переключатель) в обоих приложениях: цвета адаптируются (`--mud-palette-*` живы);
- печать (`Ctrl+P` из формы печати грида) в обоих приложениях: `@media print` работает,
  тулбар/треи скрыты;
- `grep -R "column-settings-chip\|grouping-tray\|clay-print-spinner" src/*/wwwroot/css/` →
  ни одного попадания в `app.css` приложений;
- размер: `clay.css` ≈ 1300 строк, `MedicalTests/app.css` ≈ 10–15 строк, `Kesco/app.css` — 2 строки.
