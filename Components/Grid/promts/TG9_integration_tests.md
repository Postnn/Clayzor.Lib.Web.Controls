> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# TG-INT — интеграционные на LocalDB (опционально, к G0/G2/G6)  → DynamicGridIntegrationTests.cs
```
Все методы: [Trait("Category","Integration")]. Предусловие — LocalDB со стендом из G0 (grid #140).
- ClayGridDefinitionData.LoadGridAsync(db,140,…) → Title=="Медицинские исследования", IdColumn=="КодИсследования";
- ClayGridDefinitionData.LoadColumnsAsync(db,140,…) → 5 колонок в порядке Порядок (1005 последняя);
- ClayGridUserParamsData.SaveAsync(db,0,"flt140","a") + …("b") → LoadAsync → flt140=="b" (одна строка, триггер);
- DynamicSql (G1b): QueryRowsAsync(db,"SELECT 1 AS a") → {a:1}; QueryPairsAsync на справочнике → список пар;
- в CI без БД тесты пропускаются фильтром по Trait (`dotnet test --filter Category!=Integration`).
```
