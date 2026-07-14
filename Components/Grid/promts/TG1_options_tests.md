> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# TG1 — ClayGridDynamicOptions / ClayGridSchemaMap (к G1)  → OptionsBindingTests.cs
```
- байнд из in-memory IConfiguration (Dictionary<string,string?>) с ключами вида
  "ClayGrid:Dynamic:ConnectionStringName"="Main", "...:SettingsTable"="T2" — все поля заполнены;
- дефолты схемы: new ClayGridSchemaMap().Settings.Title == "Запрос",
  .Columns.Type == "Тип", .UserParams.Name == "Параметр";
- Validate() при пустом ConnectionStringName бросает InvalidOperationException, текст содержит
  "ConnectionStringName".
```
