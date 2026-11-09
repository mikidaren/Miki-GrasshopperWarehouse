# 两套自动审批规则

## GitHub 示例入库

`validate.yml` 运行原有领域与 IFC 测试，新增数值示例测试、分类索引校验、审批策略测试及 GH 程序集编译。

`auto-approve.yml` 在该验证 workflow 完成后运行。它只检出默认分支，读取默认分支的审批脚本，通过 GitHub API 获取 PR 和文件列表。此流程需要先合入默认分支才能接收 workflow_run 事件。

自动 APPROVE、SQUASH MERGE 与删除 head 分支需要同时满足以下条件。

- 验证结果成功，事件来自 pull_request，workflow 路径为 `.github/workflows/validate.yml`。
- PR 已开放、已退出 Draft、目标为默认分支。
- 验证 SHA 与当前 PR head 一致。
- 作者是本仓库 OWNER、MEMBER 或 COLLABORATOR，账号类型为 User，head 位于本仓库。
- 变更涉及 1–100 个文件，只新增或修改 `examples/iceworld/*.json`、`examples/index.json`、`docs/warehouse-index.md` 或 `examples/iceworld/README.md`。
- 同一 head 尚未收到此机器人批准。
- 批准后、合并前再次读取的 PR 仍为当前已验证 head，且 open、非草稿、目标为默认分支。
- 合并响应确认成功。之后只删除同一仓库内的当前 PR head 分支。

代码、审批策略、CI、原生验证证据、GHX、二进制、重命名和删除均进入维护者审查。批准绑定已验证的 commit，写入批准前再次读取 PR；合并请求也绑定该 head SHA。

仓库 Settings → Actions → General → Workflow permissions 中的 “Allow GitHub Actions to create and approve pull requests” 必须启用，否则 GitHub 会拒绝机器人审批。维护者应把 `domain-and-ifc` 与 `native-build` 设为 `main` 的必需状态检查。工作流对 `contents` 使用写权限，仅用于删除成功合并后的同仓库 head 分支。

该自动流程只自动处理限定路径的数值示例与索引文档。示例 GHX、代码、策略和二进制变更仍需人工审查与合并。

## Rhino／Grasshopper 执行审批

本地配置为插件同目录的 `execution.json`，仓库模板位于 `policies/execution.json`。策略版本为 1，总生成部件上限默认 5000，`autoBake` 默认 true。将其设为 false 可关闭自动烘焙。

| 操作或条件 | 决定 |
| --- | --- |
| 有效参数的 inspect、preview，部件数在限额内 | 自动通过 |
| 有效参数的新建 bake，autoBake 已开启，示例 ID 在文档中尚未使用 | 自动通过 |
| 同 ID 已有对象、删除、替换、导出或未知操作 | 宿主明确处理 |
| 缺失策略、策略版本不支持、输入非法、部件超限 | 拒绝执行并返回原因 |

`ExecutionGate.Evaluate` 提供可移植决定。`NativeExamples.Bake` 在真正改动文档前再次执行该规则，先生成并验证封闭 Brep，再创建对象。新建流程失败时删除本次已建对象及新建的空图层和分组。源参数按米解释，烘焙时换算成 Rhino 文档单位，文档需有明确单位且公差不大于 2 mm。

`Approved Bake` 每次读取安装目录策略，通过按钮上升沿请求新建。重算和载入定义只进行预览。当前烘焙入口支持新建，更新既有对象的批准和事务由集成宿主实现。

该规则只约束使用此入口的调用。宿主接入 MCP 或 AI 工具时，应把相关工具路由到该入口，并在宿主保留人工确认操作的处理方式。
