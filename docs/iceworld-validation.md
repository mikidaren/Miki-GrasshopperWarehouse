# IceWorld 验证记录

验证日期为 2026-11-10。原生环境为 Windows、Rhino 9.0.26300.9401 和其 Grasshopper。程序集目标为 .NET 8，SDK 引用固定为 RhinoCommon／Grasshopper 9.0.26132.12305-wip。

## 可复现检查

```text
dotnet run --project tests/Domain.Tests --configuration Release
dotnet run --project tests/IceWorld.Tests --configuration Release -- --examples examples/iceworld
python tools/validate_warehouse.py
python -m unittest discover -s tests -p "test_warehouse*.py" -v
python -m unittest discover -s tests -p "test_ifc*.py" -v
node --test .github/scripts/approval.test.cjs
dotnet build src/Miki.OpenBim.Rhino --configuration Release
dotnet build src/Miki.IceWorld.Rhino --configuration Release
dotnet build src/Miki.IceWorld.Grasshopper --configuration Release
```

Python 使用独立的 3.12 环境，依赖由 `python/requirements.txt` 固定。原生适配拆分后，IceWorld 内核、目录、标准门槛与数值源测试 32 项和仓储目录 API 测试 30 项通过。原有领域与 IFC 测试继续由 CI 执行。

## 原生验证

```text
dotnet run --project tools/IceWorld.NativeQA --configuration Release -- "E:/WORK/Rhino9/System" . "<dedicated-QA-output>"
```

首个参数替换为本机 Rhino 9 的 System 目录。验证程序启动自己拥有的无窗口 RhinoCore，创建独立 Headless 文档，在输出目录生成模型与预览，并重新生成仓库中的六个 JSON／GHX 默认示例。此操作会覆盖这些生成文件。原生验证需要可用的 Rhino 授权。

| 示例 | 封闭 Brep 数量 | GHX 重新打开／求解 | 滑块修改后求解 | 毫米单位换算 |
| --- | --- | --- | --- | --- |
| 直跑楼梯与扶手 | 26 | 通过 | 通过 | 通过 |
| 折返楼梯与扶手 | 53 | 通过 | 通过 | 通过 |
| 平台栏杆 | 19 | 通过 | 通过 | 通过 |
| 排队隔离栏 | 47 | 通过 | 通过 | 通过 |
| 弧形冰墙 | 48 | 通过 | 通过 | 通过 |
| 螺旋装饰带 | 1 | 通过 | 通过 | 通过 |

合计 194 个封闭 Brep。每个模型已写出 `.3dm` 并重新打开检查有效性与封闭性。每个示例还验证了重复烘焙被拒绝且原有对象数量保持一致。

Miki OpenBIM 单入口另在独立无窗口文档中通过真实目录读取，完成六套示例预览和直跑楼梯 Bake。该检查同时覆盖索引哈希、稳定 ID、Rhino Group 与新增对象数量；没有改写仓库中的源 JSON 或 GHX。

## 组件加载回归

早期 QA 曾重复注册 Rhino 自带的 Grasshopper.dll，导致内置组件 ID 冲突提示。当前入口只注册 Miki 组件，并检查再次注册时代理数量保持一致。验证程序已重新运行通过。

额外检查包含载入时 Bake 输入为 true 仍保持预览，以及通过字节数组加载程序集的 COFF 情况下，从 Grasshopper 的库注册信息定位 `execution.json`。

`iceworld-native-validation.json` 保存运行环境、部件数量、回归结果和 SHA-256。索引中的原生通过状态与该记录绑定。新增参数组合和新的 Rhino 版本需按实际结果记录验证状态。

## 交付范围

预览图由已验证原生几何的网格投影生成，采用平行投影。模型、扶手、冰块和装饰带用于方案测试。项目尺寸、容量、结构与规范参数需后续设计确认。GitHub 审批逻辑已进行本地测试，线上事件与仓库审批权限需在规则合入后检查。

发布包可由下列命令生成。

```powershell
./tools/package-iceworld.ps1 -Destination '<new-output-directory>' -NativeQaDirectory '<dedicated-QA-output>'
```
