> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G4 — динамический режим ClayGrid: собрать грид из определения

Прочитать перед началом: `ClayGrid.razor` + `ClayGrid.razor.cs`, `ClayDataQuery.cs`,
`ClayGrid.Paging.cs`, `ClayGridPageBase.cs` (там уже есть `[Inject] protected DbManager Db` и
вызовы `Entity.GetPagedAsync<T>(Db, …)` — повторяй этот паттерн), `ClayGridDefinitionData` (G2),
`DynamicSql` (G1b).

ВАЖНО (архитектура): грид НЕ выполняет SQL сам и НЕ создаёт `DbManager`. Он инжектит `DbManager Db`
и передаёт его в классы `Clayzor.Lib.Entities` (`ClayGridDefinitionData`, `DynamicSql`).

Изменить/создать:
- В `ClayGrid` добавь параметры: `[Parameter] public bool Dynamic { get; set; }` и
  `[Parameter] public int? DynamicGridId { get; set; }`. Если `DynamicGridId` не задан — брать
  из query-параметра с именем `opt.GridIdQueryParam` (через NavigationManager).
- При инициализации в динамическом режиме (`Db` — инжектированный `DbManager`, `opt` — опции):
  1. `def = await ClayGridDefinitionData.LoadGridAsync(Db, gridId, opt.SettingsTable, opt.Schema)`;
     если null — показать сообщение «грид не найден».
  2. Заголовок грида = `def.Title`; рядом иконка из `def.IconUrl` (если задана). Разметка —
     существующим способом вывода заголовка грида, стили по STYLE_RULES.md (класс, не инлайн).
  3. `cols = await ClayGridDefinitionData.LoadColumnsAsync(Db, gridId, opt.ColumnsTable, opt.Schema)`;
     для каждой построй колонку грида, тип — через `ClayColumnTypeMap.Resolve(col.Type)`.
     Колонки с `Order` NULL/0 — скрыты (не в вывод).
     Неподдержанные типы (Resolve==null) — пропусти с логом (реализуешь в фазах 4–5).
  4. Источник данных — `def.Sql`, выполняется ТОЛЬКО через `DynamicSql.QueryPagedRowsAsync` /
     `QueryCountAsync` (Lib.Entities, G1b), которым передаётся `Db`. Подключи к СУЩЕСТВУЮЩЕМУ
     пути данных (`ClayDataQuery`/пагинация), не пиши новый источник.
- Порядок колонок по `Order` (по возрастанию).

Не делай: не трогай статический режим грида (когда `Dynamic==false` — поведение прежнее);
не создавай альтернативный источник данных; НЕ вызывай Dapper/SQL напрямую из Controls и НЕ
создавай `DbManager` — только вызовы классов `Clayzor.Lib.Entities` с передачей `Db`.

Проверка (ручная):
- открыть страницу с `?id=140` → грид с заголовком «Медицинские исследования», колонками
  №/Название/Создано/Тип исследования (id/name/created/type видимы, active скрыт), данными из Sql;
- колонка с Порядок=0 (active) в гриде не показана.
