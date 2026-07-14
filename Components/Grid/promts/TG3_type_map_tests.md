> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# TG3 — типы/оператор/Формат (к G3)  → ColumnTypeMapTests.cs
```
[Theory] по Тип 1,2,3,4,7:
- ClayColumnTypeMap.Resolve(тип) не null и соответствует ожидаемому дескриптору;
- Resolve(5),Resolve(6),Resolve(8..13) == null; IsSupported(5)==false;
- ClayDefaultOperator.For: Text→Contains, Number→Equals, Date→Equals, Bool→Equals;
- ClayColumnFormat.Parse(1,"N2")→"N2"; Parse(3,"dd.MM.yyyy")→"dd.MM.yyyy"; Parse(7,"Активно=1")→"Активно=1".
```
