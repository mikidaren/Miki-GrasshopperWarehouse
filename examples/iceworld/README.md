# 冰雪大世界 Grasshopper 示例

这组示例用于方案阶段的参数化建模。源长度单位为米，角度单位为度。所有默认尺寸均为测试假设，实际项目尺寸与容量待定。

## 安装与打开

1. 使用 Windows Rhino 9 和 Grasshopper，Rhino 使用 .NET 8 或兼容运行时。
2. 发布包用户把 `plugin/` 放在单一安装目录。Grasshopper 用户将该目录加入 Components Folder，并保留同目录中的 `Miki.IceWorld.dll`、`Miki.IceWorld.Rhino.dll`、`execution.json` 和 `iceworld-standard-baseline.json`。Miki 插件直接引用 `Miki.IceWorld.Rhino.dll`，接口见 `docs/plugin-integration.md`。
3. 下载包如被 Windows 标记为阻止，先在 ZIP 属性中解除阻止，再解压。重启 Rhino。
4. 打开任意 `.ghx`，调整左侧滑块，观察 Rhino 预览与右侧参数报告。
5. 按 `Button` 后，`Approved Bake` 读取安装目录的 `execution.json`，校验参数并新建 Rhino 对象。文档中的长度会按单位自动换算。

Libraries 中保留一份插件。`Grasshopper.dll`、`GH_IO.dll` 和 `RhinoCommon.dll` 由 Rhino 提供。发布包只包含 Miki 插件和它的领域程序集。

如果启动时弹出 `Component ID conflict`，并同时指向同一个 Rhino 自带 `Grasshopper.dll`，先点击 `Skip All`。关闭 Rhino，打开 File → Special Folders → Components Folder，确认 Miki 文件只安装一份。若 Components Folder 中误放了 `Grasshopper.dll`、`GH_IO.dll` 或 `RhinoCommon.dll` 副本，只删除这些副本，保留 Rhino 安装目录 `Plug-ins\Grasshopper` 下的原文件。清理重复的 Miki 旧版本后重启 Rhino，再重新打开 GHX。若冲突仍指向 Rhino 安装目录中的原文件，应使用当前发布包，旧 QA 重复注册内置程序集的问题已在其中修正。

## 示例目录

| 文件 | 内容 | 默认测试假设 |
| --- | --- | --- |
| `straight-stair.ghx` | 直跑楼梯、上下平台、双侧扶手与立柱 | 高差 1.8 m，水平梯段 3.6 m，宽 1.5 m，12 个踢面 |
| `switchback-stair.ghx` | 折返双跑、休息平台、连续双侧扶手 | 总高差 3.6 m，每跑 3.6 m，每跑 12 个踢面，梯井 0.2 m |
| `platform-railing.ghx` | 三边栏杆与观景平台，入口边敞开 | 6 × 4 m，标高 1.8 m |
| `queue-barriers.ghx` | 蛇形通道隔离栏，交替预留转弯口 | 长 8 m，4 条通道，通道宽 1.5 m，转弯口 1.8 m |
| `curved-ice-wall.ghx` | 弧形墙的分层分块及块缝 | 半径 5 m，圆心角 120°，高 2.4 m，厚 0.5 m，12 段 × 4 层 |
| `spiral-ornament.ghx` | 圆柱包络上的螺旋装饰带 | 半径 2 m，高 3 m，3 圈，带半径 0.04 m |

`.json` 保存可调用的数值参数，`.ghx` 保存带滑块的画布。两种入口均使用相同的编译内核。修改 JSON 会影响使用该 JSON 的调用，修改 GH 滑块会影响当前 GH 定义。需要同步默认值时重新生成 GHX，并更新索引验证记录。

当前六套文件继续标记为 `concept-test-assumptions`，保持既有几何和验证哈希。后续新增或迁移到中国标准参数轮廓的示例使用 `standard-candidate`，并通过同一目录 API 在 Bake 前执行标准门槛。

## 参数与输出

每个 GH 定义分成三组，输入、几何生成、输出与审批。`Geometry` 输出封闭 Brep，`Roles` 与对象逐项对应，`Source` 输出可读 JSON，`Report` 说明几何数量与测试假设。

- `rise` 在直跑示例中表示单跑高差，在折返示例中表示总高差。
- `run` 为每跑的水平总长度。每跑生成 `risers` 个阶梯实体，每段进深为 `run / risers`，顶部踏面与上平台同高。
- `railHeight` 是扶手中心线相对踏步前缘或平台的竖向高度。
- `railRadius` 是扶手和立柱的管半径，`postSpacing` 控制沿平面路径的立柱布置目标间距。折返平台连接处另设转角立柱。
- 弧形冰块用多段平面轮廓近似圆弧。当前块缝上下对齐，错缝排块待扩展。
- 螺旋带沿圆柱包络生成，当前输出独立装饰实体。

滑块范围是生成器输入范围。楼梯还检查每级高差不大于 0.4 m、进深不小于 0.1 m。组合参数不满足校验时，组件报告错误。扶手高度、立柱间距及平台入口需结合项目用途另行设计。结构承载、防滑、冻融、疏散、栏杆开口与施工连接均需专项校核。

## 执行审批

GH 重算只更新预览。烘焙由按钮上升沿触发，批准后建立按示例 ID 命名的 Rhino Group，并按 `step`、`handrail`、`post` 等角色分层。每个对象附带示例 ID、角色和参数源。

同一文档已有相同示例 ID 时，执行入口返回需要宿主处理更新的决定。需要多个独立实例时，宿主应提供不同的合法 ID。当前示例按钮负责新建流程。

完整规则见 `docs/approval-rules.md`，分类规则见 `docs/warehouse-index.md`，原生证据见 `docs/iceworld-validation.md`。

<!-- automatic-approval-smoke -->
