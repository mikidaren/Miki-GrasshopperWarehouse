# Warehouse 分类、分组与索引规则

`catalog.json` 列出工具族与 API。`examples/index.json` 列出可直接使用的示例。每个示例占一条索引记录。

## 参数标准基线

`policies/iceworld-standard-baseline.json` 保存中国标准的机器可读依据和候选参数值，说明见 `docs/iceworld-standard-baseline.md`。`candidate` 表示参数待适用专业复核，不代表示例或方案已经取得工程合规结论。消防疏散、结构、无障碍、冻融耐久及冰雪景观专项事项保留为明确的人工复核项。

## 稳定身份

- `id` 使用大写 ASCII 字母、数字、点、下划线和连字符，最多 128 字符。示例以 `IW-` 开头，例如 `IW-STRAIGHT-STAIR`。
- ID 在改名和参数调整后保持稳定。新的独立算法使用新的 ID。
- `kind` 与内核生成器名称一致，文件名使用小写连字符。源文件和 GH 文件使用相同词干。
- `title` 用于中文浏览，`tags` 使用小写英文便于检索。

## 主分类

| 分类键 | 中文分组 | 当前示例 |
| --- | --- | --- |
| `circulation/stairs` | 交通／楼梯 | 直跑、折返 |
| `circulation/railings` | 交通／扶手栏杆 | 观景平台栏杆 |
| `visitor-flow/queues` | 游客组织／排队 | 蛇形隔离栏 |
| `ice-construction/blocks` | 冰构件／分块 | 弧形冰墙 |
| `surface/ornament` | 表皮／装饰 | 螺旋带 |

每个示例使用一个主分类，形态、材料意向和使用场景写入标签。新增分类时同步索引与本表。

## 必填字段

索引包含 ID、生成器、中文标题、主分类、标签、源路径、GH 定义路径、单位、运行环境、设计阶段和验证记录。路径必须位于 `examples/iceworld/`，并指向存在的 `.json` 或 `.ghx`。每个源文件与定义文件只归属一条记录，新增文件需要入索引。

当前单位为 `m`，运行环境为 `rhino9-net8-grasshopper`，阶段为 `concept-test-assumptions`。参数名称、默认值、范围及整数要求由 `ExampleEngine.Parameters` 定义，画布滑块与 JSON 使用同一组参数。

## 画布和 Rhino 分组

GH 画布使用 `01 INPUTS`、`02 GENERATION`、`03 OUTPUT / APPROVAL` 三组。输入从左向右连接，输出保留 Geometry、Roles、Source 和 Report。

Rhino Group 使用 `MIKI-IW::<示例ID>`。角色图层使用 `MIKI-IW-<role>`，对象名称包含示例 ID、角色和序号。对象属性保存参数源，便于宿主检查对象归属。

## 验证状态

`portable`、`native`、`grasshopper` 分别采用 `passed`、`pending`、`failed`。数值源检查、原生几何检查和 GH 打开求解分别记录。

当原生或 GH 状态为 passed 时，索引必须保存当前源与定义的 SHA-256，并匹配受维护者审查的 `docs/iceworld-native-validation.json`。证据还绑定内核源代码摘要。修改参数、GH 定义或内核后，应重跑原生检查，或将相关状态改为 pending。`tools/validate_warehouse.py` 会拒绝过期证据。

```text
dotnet run --project tests/IceWorld.Tests --configuration Release -- --examples examples/iceworld
python tools/validate_warehouse.py
```
