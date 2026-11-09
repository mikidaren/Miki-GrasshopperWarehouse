# Miki 插件参数化调用

Miki Rhino 插件通过两个稳定 DLL 使用本仓库：

| DLL | 依赖 | 用途 |
| --- | --- | --- |
| `Miki.IceWorld.dll` | 无 Rhino 依赖，.NET 8 | 示例索引、参数校验、执行规则和中国标准候选门槛 |
| `Miki.IceWorld.Rhino.dll` | RhinoCommon 9、上述 DLL | 目录读取、Rhino Brep 生成、自动审批 Bake |

`Miki.OpenBim.Rhino.dll` 已引用这两层，并提供 `RhinoModel.BuildIceWorld` 与 `RhinoModel.BakeIceWorld` 两个入口。Miki 插件仍负责工具栏、命令、界面、撤销与 UI 线程调度。

## 安装目录

插件可以采用下列目录结构。根路径可由产品配置指定，仓库内容不需要重新编译进 `.rhp`。

```text
Miki-Warehouse/
  plugin/
    Miki.IceWorld.dll
    Miki.IceWorld.Rhino.dll
    Miki.OpenBim.dll
    Miki.OpenBim.Rhino.dll
    execution.json
    iceworld-standard-baseline.json
  examples/
    index.json
    iceworld/*.json
    iceworld/*.ghx
  policies/
    execution.json
    iceworld-standard-baseline.json
```

加载时验证索引、所有示例 SHA-256、数值参数和标准配置。未知 ID、路径逃逸、过期哈希和不完整基线都会被拒绝。

## 插件调用

```csharp
using Miki.IceWorld;
using Miki.IceWorld.Rhino;
using Miki.OpenBim.Rhino;

var catalog = WarehouseCatalog.Load(warehouseRoot);

// 只预览和检查，不修改文档。
var preview = IceWorldRhino.BuildFromCatalog(
    catalog,
    "IW-STRAIGHT-STAIR",
    new StandardDesignContext(
        StandardDesignContext.StandardCandidate,
        OutdoorEgressStair: false));

// 通过 Miki OpenBim.Rhino 单入口 Bake。
JsonObject result = RhinoModel.BakeIceWorld(
    RhinoDoc.ActiveDoc,
    warehouseRoot,
    "IW-STRAIGHT-STAIR",
    new StandardDesignContext(StandardDesignContext.StandardCandidate));
```

`RhinoModel.BuildIceWorld` 返回几何数量、角色、阶段、标准检查项和报告。`BakeIceWorld` 返回新对象数量和 GUID。

## 阶段与标准

索引中的 `stage` 决定烘焙门槛：

| 阶段 | 行为 |
| --- | --- |
| `concept-test-assumptions` | 保持既有概念示例，允许新建对象 Bake；标准检查记录为提示 |
| `standard-candidate` | 运行时检查楼梯踏步、梯段级数、梯宽、栏杆高度及室外疏散楼梯倾角；任何阻断项均禁止 Bake，预览仍可用 |

`StandardDesignContext.OutdoorEgressStair` 默认是 `false`。只有项目明确把该梯作为室外疏散楼梯时才设为 `true`。室内、室外和疏散用途不能由示例 ID 猜测。

标准检查只约束参数化输入。项目仍需独立完成消防疏散、结构安全、无障碍、冻融耐久及 GB 51202-2016 专项审查。`standard-candidate` 不表示整馆合规。

## 部署约束

- `.rhp` 读取本地已审核快照。更新仓库时，应先完成 CI、索引哈希和示例版本切换。
- 网络下载与自动更新不属于运行时 API。宿主如需在线分发，应单独完成签名、版本锁定、回滚和缓存策略。
- Rhino 文档必须有明确长度单位，公差不得大于 2 mm 等值。源长度按米解释，Bake 时转换。
- 同一示例 ID 已在文档中存在时，自动 Bake 拒绝覆盖。更新需由宿主提供受控替换流程。
- `Miki.IceWorld.Rhino.dll` 与现有 Rhino/Grasshopper 自带的 `RhinoCommon.dll` 配套使用，发布包不复制 Rhino 内建程序集。

## Grasshopper

发布包的 `plugin/` 同时提供 `Miki.IceWorld.Grasshopper.gha`。GH 组件和 Miki `.rhp` 调用同一个 `IceWorldRhino` 实现。启动时出现同名 Rhino 内建组件冲突并同时指向 Rhino 安装目录的 `Grasshopper.dll` 时，选择 **Skip All**，再清理 Components Folder 中的重复内置程序集副本。
