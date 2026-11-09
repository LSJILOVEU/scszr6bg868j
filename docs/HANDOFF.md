# WarCore 接手说明（给外部 AI）

本文件自包含：只看本仓库就能接手。正文中文，末尾附英文要点。

## 0 一句话

WarCore 是一个可换皮的俯视角（等距视角）战争游戏底座，Unity 6000.6.0f1 + C#。仿真核心确定性、与 Unity 解耦，可在任何 Linux/macOS 机器上用 .NET 8 无头测试。玩法先对标 Running With Rifles（RWR，只对标玩法，不用其素材），之后换成其他题材。

## 1 总目标（不可偏离）

1. **底座 = 自己的引擎。** 以后所有游戏共用它：RWR 式战争、忍者题材（手里剑、技能、冲刺、瞬移）、古代冷兵器战争、修仙，乃至俯视角开放世界。底座要模块化、灵活、正确；美术往后放。
2. **用 RWR 玩法检验底座。** 先做出和 RWR 一样的玩法，再换皮。RWR 特有的东西放内容包（`Assets/WarCore/Content/Rwr`），底座只放各题材通用的机制。换皮检验的标准：只加内容包，底座目录 diff 为空；做不到的写成缺口清单，不在换皮包里硬改底座。
3. **最终水准是 RWR2/RWR3。** 和 RWR 一样只是中途关卡。
4. **零代码做地图。** 不会写代码的人能在 Unity 里（或先让 AI 生成）做出完整战争地图：山路、悬崖、丘陵沟壑、沙漠、泥地等；摆士兵和据点，点运行就看到 AI 自动打起来。地图可程序辅助生成，但必须能手改，手改结果不被覆盖；可见地形、碰撞、导航、据点数据必须一致。
5. **玩法核心：** 玩家控制一个兵（等距视角，镜头 45° 可调），其余由 AI 打；AI 用真射线、有散布、会被墙和地形挡；友军伤害是开关；单图边长上限 512 m。
6. **做深不做广。** 先把已有机制做对、做到可玩，再加新机制。不加和当前任务无关的防御代码。

## 2 现在做到哪了

主干 `main` 最新提交见 `git log`（本文写作时为 `L10` 合入，PR #60）。粗估全路线约 76 个工作包，已完成约 47 个（估计，不精确）。整体处于“第一个可玩版本（VS1）接近完成、还没有真人试玩过”的阶段。

已合入的主要能力（按块，括号里是 PR 标题中的包号，可在 `git log --oneline` 里查到）：

- **确定性仿真核心**：定点数学、实体存储、快照与状态哈希、同种子同结果、`Sim.Step` 零分配（M0–M2）。
- **地图与寻路**：地图字节格式与墙、网格 A*、人群移动与扫掠碰撞、地图描述文件烘焙成 `.wcmap`、地图门禁、手改保护（E1a/E2/E3，#24–#33）。
- **战斗**：即时命中射击、散布、友伤开关、近战挥砍与突刺、格挡与格挡 AI、开火决策接缝 `IAutoFire`（#15–#23）。
- **对局**：据点占领、补员、胜负、规则模式（M6）；100 对 100 性能（M7）。
- **装配与内容**：按特性表装配世界（安装器）、内容按段加载（json → ContentPack）（#21、#22、#29）。
- **玩家**：等距相机与拾取、HUD、玩家移动、瞄准射击、格挡/投掷/技能键位（只接到输入，技能未实现）、玩家重生（切到预备兵，#38）、重生倒计时 HUD（C2）、重生点避开有敌人的据点（C1b）、重生无敌时间（C1c）。
- **AI**：寻路失败退避（L4）、地表遮挡缩短发现距离（A2）、目标记忆/最后已知位置（A3）、搜索最后已知位置（A3a）、反应时间（P1）、感知间隔（P2）、点射节奏（P3）、朝最后已知位置压制射击（A3b）。这些都是“默认关”，由数据打开。
- **地形**：高度场与地面查询、单位在地形上走、出生/部署读地面、地形驻守夹具（L5）、地形世界整局（L6）、Unity 地形网格（L7）、地面点/弹道/射击视图贴地（L8、L8b）、高度场挡视线和子弹（L9）、玩家瞄准高低差（A4）、玩家在地形上走（L10）。
- **工具**：变异测试工具 `tools/mutation`（#37）、隐私守卫 `tools/pii-guard`、交火峰值基线 `docs/peak-baseline.md`。

已知遗留问题（真实存在，可做成任务）：

- 生成次序偏差：在 C1b/C1c 的样本里，后生成的一方（单位键 151–300）总是赢，原因未定位（见 PR #61 正文末段）。
- 冲刺穿人、贴墙卡住；重生时可能撞上开枪请求容量上限。
- M7 演示里 52 人驻守不到位；`M6` 的反应/感知/点射三项（D8–D10）占位值未按 RWR 录像校准。
- 10 条碰撞 oracle 测试 CI 和 Unity 都不自动跑，改碰撞代码要手动跑并在 PR 里写明。
- AI 不会主动追击近战（A1 未做）；格挡没接 Unity 输入到玩法层。
- 变异测试还没进 CI（F1b 未做），现在靠 PR 作者本地跑并贴结果。

## 3 下一步候选（按建议优先级）

工作包编号沿用已有命名。未排进来的放候选池，不硬排。**先做排在前面的，一次只做一个目标。**

| 优先 | 包 | 做什么 | 环境 |
|---|---|---|---|
| 1 | 偏差排查 | 定位“后生成一方必胜”的原因（遍历顺序、目标选择平局、占领判定先后等），先写能复现的测试，再修；修法若改行为须单独列出 | H |
| 2 | F6 | 单位按兵种 id 生成：新增格挡段；兵种段加 speed、block、archetype 可选字段；`UnitSpawn.OfType(...)`；悬空 id 报错写明包、条目、字段。旧构造方式造的世界哈希不变 | H |
| 3 | R2 | 对局规则零件按字符串 id 从数据选（现为 `Battle/MatchRules.cs` 里的内部类）；`rwr.m6-demo`、`test.never-ends` 的 id 字节不变；全部固定哈希不变 | H |
| 4 | V1 → V2 | 换皮检验：古代近战内容包（一个阵营、两种兵、一把挥砍武器，用 F6 摆兵），新增 C# 类 = 0；再选已有零件组成自己的胜负规则 | H |
| 5 | S1 → V3 | 皮肤映射最小版（兵种 id → 颜色/形状/名字，不碰 Sim）；两套内容包在同一张图跑通，交缺口清单 | U |
| 6 | F1b | 变异门槛进 CI：PR 新增或改动的 `tools/mutation/lists/*.txt` 全部杀死才绿；没改清单的 PR 也要报成功；改 `.github/` 的 PR 只能由仓库所有者手动合并 | H |
| 7 | A4b | 瞄准预览（地形抬高线、墙中心线，被挡变红）与 Unity 接线、截图录屏 | U |
| 8 | M7 → M8 → M9 → M10 | 冲刺（含冲刺输入位，只有旧字段时快照字节不变）→ 载具/坐骑上下与座位跟随 → 骑乘控制与坐骑死亡 → 马、车、飞剑三份数据（只改数据） | H |
| 9 | R3–R6 | 无据点/无 AI 世界能跑；关键实体与斩首；计时器与守塔；战场事件最小版 | H |
| 10 | E4–E8 | Unity 地图编辑器：高度笔刷、表面笔刷、摆墙/据点/出生点、按兵种名摆兵、一键烘焙运行 + 一致性自检（0 差异） | U |
| 11 | A1、PF1、P5、P6 | AI 近战追击（默认半径 0 = 关）；玩法翻转（在 RWR 包里把 P1–P3/A1 设为占位值，**唯一允许重录行为基线的包**）；视线候选走空间格子；交火峰值 ≤4 ms | H |
| 12 | C6–C9、X2 | 最小投射物（固定容量池）；投掷；一个主动技能经出手意图管线；格挡示例技能（只改数据）；衰减参数与兵种枚举核实 | H |

候选池（小项，可插队，需单独成包）：地表着色；玩家指挥的兵单独反应开关；文案表中文版；点射停顿跟人走；近战意图方向；压制散布擦到队友；存档边界变异盲区；地形局镜头跟随；HUD 的 Ground 项在地形图上为空；截图协程异常不退出；死后自选重生点；鼠标瞄高坡与弹线真实高度；换皮检验提前做最小版。

H = 无头，任何 Linux/macOS 机器可跑；U = 需要交互式 Unity 编辑器。

## 4 去哪里干活

1. 克隆本仓库，基于最新 `main` 开分支，一个工作包一个分支，分支名建议 `ext/<包号>-<短名>`（例：`ext/f6-unit-type-spawn`）。
2. 一次性安装钩子：`sh tools/pii-guard/install.sh`。
3. 提交身份必须是 `warcore <noreply@users.noreply.github.com>`，时间用 UTC：
   ```sh
   git config user.name warcore
   git config user.email noreply@users.noreply.github.com
   TZ=UTC git commit -m "Add unit spawn by type id"
   ```
4. 装 .NET 8（Ubuntu：`apt-get install -y dotnet-sdk-8.0`；macOS：`brew install dotnet@8`），跑测试：
   ```sh
   dotnet build tools/headless/WarCore.Headless.Tests.csproj -c Release
   dotnet test  tools/headless/WarCore.Headless.Tests.csproj -c Release --no-build \
     --filter "TestCategory!=StackBound&TestCategory!=Manual"          # 全量约 2.2 万条，约 20 分钟
   dotnet test  tools/headless/WarCore.Headless.Tests.csproj -c Release --no-build \
     --filter "TestCategory=StackBound"                                # Linux
   dotnet test  ... --filter "FullyQualifiedName~C1c"                  # 子集，几秒
   ```
5. 推分支，开 PR 到 `main`。`main` 受保护：只许 squash 合并，禁止强推，禁止直推。
6. 攒一批改动再推：每次推送都会触发 Ubuntu + Windows 双平台 CI。

本文件即仓库所有者的许可：外部 AI 可以在本仓库开 PR。不要开 issue，不要在别人的 PR 上评论。

## 5 必须遵守的规则

### 5.1 公开仓库隐私规则（最高优先，详见 `CLAUDE.md`）

- 仓库里任何地方（提交信息、PR 标题和正文、评论、代码注释、文档、文件名）都**不得**出现：人名/昵称/账号名、邮箱、作息与时区、地点、机器或硬件信息、本地路径、账单或账户信息、聊天或 AI 会话链接、会话 id。
- 不加签名、`Co-Authored-By`、会话链接、“Generated with …”之类的署名行。用的 AI 工具如果自动加，提交和 PR 前删掉。
- 不写“谁要求的、为什么”这类个人化描述；只写技术内容，越少越好。
- 提交信息：一行，祈使句，≤72 字符；需要正文时 ≤8 行。
- 每次提交和推送前跑 `python3 tools/pii-guard/pii_guard.py --staged`（钩子会自动跑）。有发现就改内容；**禁止** `--no-verify`，禁止改守卫让它通过。私有词表不在仓库里，本地只跑通用规则，CI 会用完整词表再查一遍。
- 截图、录屏等媒体文件不进仓库（守卫会拦）。

### 5.2 合并门槛

PR 只有同时满足以下几条才会合并：

1. **CI 双平台全绿**：`headless-tests`（Ubuntu + Windows）、`pii-guard`、`privacy-gate`。
2. **变异测试全部杀死（硬门槛）**：每条新规则至少一条测试；在 `tools/mutation/lists/<包号>.txt` 写变异清单（每条只改一行生产代码，并点名应该变红的测试，格式照现有清单），本地跑 `python3 tools/mutation/mutate.py tools/mutation/lists/<包号>.txt --out <仓库外的文件>`，必须全部被断言失败杀死，结果贴进 PR。CI 暂时不自动跑这一步（F1b 未做），由审查方复跑。只能在 Linux/macOS 跑，仓库须无未提交改动。
3. **测试卫生**：`python3 tools/mutation/hygiene.py origin/main HEAD` 必须 PASS（拒绝弱断言写法）。
4. **Unity EditMode failed=0**：改到 `Assets/WarCore/Unity/**`、场景、或任何会被 Unity 编译的代码时，要在 Unity 6000.6.0f1 里跑 EditMode 测试，结果 XML 里 `failed="0"`。没有 Unity 的话在 PR 里写“需要 Unity EditMode 验证”，由仓库维护方代跑；不过不合。注意：无头测试用 NUnit 3.14/.NET 8，Unity 用 NUnit 3.5/netstandard，只有 Unity 有的 API 差异无头抓不到。
5. **审查通过**：维护方做代码审查和隐私审查，审查里写 `Privacy: PASS <头提交前 7 位>` 并加 `reviewed` 标签。**外部 AI 不得自己写 PASS 行，不得自己加 `reviewed` 标签。** 新推送会自动去掉标签，需要重审。

### 5.3 行为锁（确定性）

- **确定性锁永久不许改**：同种子同结果、快照与状态哈希一致、M1–M4 的固定值、M5 的定点数学与开火节奏向量。
- **行为基线**（随玩法变的结局哈希，如 M5 交火结局、M6、M7 的固定哈希和 `M7BehaviorLockTests` 记录）：只有任务明确写“可重录”的包才能重录；PR 里逐项列出旧值、新值和原因，每个被替换的哈希配一条语义断言（胜方、结束 tick、死亡数）。**不许为了过测试调参数。** 纯重构包基线必须一字不变。
- 既有测试不删、不跳过、不放宽断言（任务点名替换的除外，且在 PR 里列出）。
- `Sim.Step` 零分配相关测试必须仍过。

### 5.4 写代码的规则

- **新玩法默认关**：新机制放在可选组件/世界选项/数据字段后面，缺省值等于旧行为；没打开它的世界，状态字节、快照、哈希、逐 tick 行为全部不变，并要有一条“关着时不变”的锁定测试。
- 新状态放进只在打开时注册的独立系统；系统 `Order` 不能和已有的重复（`Sim/Sim.cs` 会拒绝）。
- 新增存档字段要进快照、状态哈希和版本号；旧快照要能读或被明确拒绝。
- RWR 特有的数值、规则放 `Content/Rwr`，底座目录不出现题材专用概念。
- 占位数值在代码或数据里标 `PLACEHOLDER`，并在 PR 里列出。
- 不加和任务无关的防御代码、重构或格式化；一个 PR 一个目标。
- 任务规模参考：生产代码净改动约 ≤400 行（不含测试）、生产文件 ≤8 个、新增公共类型 ≤3 个；超了就拆。
- 改碰撞代码时，手动跑那 10 条碰撞 oracle 测试并写进 PR。

### 5.5 PR 正文格式（照最近合入的 PR，例如 #61）

依次写：一句话说明是否改行为 → `## Changes` → `## Re-recorded behaviour baseline`（有才写，旧值/新值/原因表）→ **`## Departures from the work package`（必写：所有和任务描述不一致的地方，包括夹具、数值、测试序列的偏离；没有就写 “No departures”）** → `## Base and commits`（基点提交、各提交用途、头提交）→ `## Files`（生产、测试、工具分开列）→ `## Tests`（每条命令及通过/失败数、变异结果 `x/x killed`、卫生检查、Unity 状态）→ 验收对照表（验收项 | 测试名 | 关键 tick | 碰到的已锁测试 | 杀死的变异）。

PR 正文同样只写技术内容，不加署名行。

## 6 仓库结构速览

```
Assets/WarCore/
  Sim/            确定性核心：实体存储、定点数学、快照/哈希、调度、输入编码（改这里要最谨慎）
  Interim/        移动：寻路跟随、人群、扫掠、惯性、地形上行走、朝向
  Combat/         战斗：射击/近战/格挡结算、视线、开火决策接缝 IAutoFire、重生无敌
  Battle/         对局与 AI：据点占领、补员、胜负规则、重生、SimpleAi、感知/记忆/搜索
  Control/        玩家：控制切换、移动、开火、无人接管
  Content/        内容与地图：ContentPack/段加载、RWR 包（Content/Rwr）、.wcmap 读写与校验、地形规则
  Maps/           地图描述 json（m6/m7/terrain 演示）
  Presentation/   Unity 无关的桥：SimSession、WorldBuilder/WorldRecipe、Features（安装器）、相机与拾取、视图数据
  Skin/           皮肤（目前几乎为空，S1 要做）
  Unity/          MonoBehaviour 宿主（MpHost 等）、Editor 工具、Unity 专用测试、场景
  Verification/   哈希向量、性能与分配取证
  Tests/          EditMode 与 MapContent 测试（无头和 Unity 共用）
Assets/StreamingAssets/maps/   烘焙后的 .wcmap
tools/headless/   .NET 8 无头测试工程（与 asmdef 同样的程序集划分）
tools/mutation/   mutate.py（变异）、hygiene.py（测试卫生）、lists/（每个包的变异清单）
tools/pii-guard/  隐私守卫与 git 钩子
docs/             peak-baseline.md（交火峰值基线）、本文件
.github/workflows headless-tests / pii-guard / privacy-gate
```

入口：Unity 菜单 `WarCore/MP/Play` 是玩家可玩版本（M7 地图，`MpHost` 驱动）；`WarCore/L10/Terrain Player` 是地形上的玩家版本；其余 `WarCore/<包号>/Play` 是各阶段演示。演示世界的配方在 `Presentation/Bridge/DemoRecipes.cs`。

## 7 回交方式

1. **拆任务的 AI**：每个任务写成一张卡，包含：包号、目标（推进第 1 节哪一条）、做什么、不做什么（及其去向）、前置、可改文件清单、验收（≤6 条，每条可机械检查：测试名、数值、哈希、字节）、必杀变异、基线是否可重录、环境 H/U。关于现有代码的每个说法附 `文件:行`；拿不准的写“不确定”，不要猜。设计决策不放进执行任务里，先单独定。
2. **执行的 AI**：一张卡一个分支一个 PR。完成后 PR 里按第 5.5 节写清楚；需要 Unity 或需要真人看录屏的，在 PR 里写明。
3. **交还给维护方的只有三样**：PR 链接、一两句结论、未解决的问题清单。截图和录屏不进仓库，另行交付。
4. **遇到以下情况停下来，写进 PR 或回报，不要自行决定**：要改确定性锁；要重录未授权的基线；要改 `.github/`、`CLAUDE.md`、`tools/pii-guard/`；任务目标和第 1 节冲突；推送被拒。

---

## English summary

- WarCore: deterministic, reskinnable top-down war-game foundation (Unity 6000.6.0f1, C#). First prove it by matching the gameplay of Running With Rifles, then reskin (melee ancient war, ninja skills, cultivation, open world). Theme-specific content goes in content packs; the foundation stays generic. Zero-code map making is a hard goal.
- Status: ~47 of ~76 work packages merged (estimate). Player control, respawn, AI perception/memory/burst/suppression, terrain walking, height-field line of sight and aim are in. Nobody has play-tested the build yet.
- Next: spawn-order bias fix, F6 unit spawn by type id, R2 data-selected rule parts, V1/V2 reskin test, S1/V3 skins, F1b mutation gate in CI, A4b, M7–M10 dash and mounts, R3–R6, E4–E8 map editor, A1/PF1/P5/P6, C6–C9.
- Work: branch from `main` (`ext/<id>-<name>`), commit as `warcore <noreply@users.noreply.github.com>` with `TZ=UTC`, PR to `main` (squash only).
- Gates: dual-platform CI green; every mutation in `tools/mutation/lists/<id>.txt` killed; `hygiene.py` PASS; Unity EditMode `failed=0` when Unity code is touched; maintainer privacy review (`Privacy: PASS <sha7>` + `reviewed` label; never add these yourself).
- Rules: no personal data or attribution lines anywhere; never bypass the guard; determinism locks are permanent; behaviour baselines only re-recorded when the task says so, with old/new values listed; new mechanics default off with an unchanged-when-off lock test; no unrelated defensive code; always write a Departures section.
