> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# TG2 — мапперы и строители SQL определения/колонок (к G2)  → DefinitionMapperTests.cs
```
- ClayGridDefinitionData.MapDefinition на словаре строки (ключи = имена из схемы) → все поля ClayGridDefinition верные;
- MapColumn на строке с Порядок=0 → ClayColumnDefinition.Order==0 (не отброшено), Type==7;
- BuildColumnsSql/BuildGridSql: имена берутся из схемы и обёрнуты в [], параметр @gridId присутствует;
- MapColumn с изменённым ClayGridSchemaMap (напр. Header:"Caption") читает значение из колонки
  "Caption" — подтверждает, что имена берутся из схемы, а не захардкожены.
```
