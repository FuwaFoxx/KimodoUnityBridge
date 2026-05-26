# Kimodo Unity Bridge User Manual (Draft)

## 1. 建议必须写入 Manual 的 Editor 面板

1. `Project Settings > Kimodo Server Manager`
2. `KimodoPlayableClip` Inspector（Timeline 动画片段的 Inspector）
3. `Tools/Kimodo/Animator/Split Transition And Insert Generated Motion...`
4. Constraint Marker Inspector（`Root2D / FullBody / EndEffector`）
5. `Kimodo Constraint Override Edit` 窗口（约束编辑会话窗口）
6. `KimodoBVHLoader` Inspector（BVH 预览工具）
7. Timeline Floating UI Overlay（可开关的浮动提示/输入 UI）

---

## 2. 每个面板应说明的功能点

## 2.1 Kimodo Server Manager

入口：`Project Settings > Kimodo Server Manager`

需要写的功能：
- Runtime 根目录检测与创建（`Create Kimodo Server`）
- 模型与启动参数设置（`Model`, `VRAM Mode`）
- 全局生成设置（`Max Cached Clip`, `Generate Timeout`, `Enable Floating UI`）
- 本地模型路径覆盖（`Local Models Path` + `Browse`）
- 服务启停（`Start Server` / `Stop Server`）
- 模型检测与删除（`Detected Models` 列表）
- 维护操作（`Try Fix`、`Delete All Data`）

## 2.2 KimodoPlayableClip Inspector

入口：Timeline 中选中 `KimodoPlayableClip` 资产/片段

需要写的功能：
- 生成后端选择（Bridge / ComfyUI）
- 生成参数（Prompt、Duration、Diffusion Steps、Random/Seed、Bridge Model、VRAM）
- In-between Interpolation（边界姿态引导）
- 约束引用显示（Constraint References）
- 生成与取消（`Generate & Bake` / `Cancel`）
- 桥服务快捷控制（`Close Bridge Server`）
- 动画属性（Clip、Foot IK、Loop）
- 烘焙与重定向（Auto Retarget On Binding、Custom Avatar）
- 高级曲线过滤（Reduce Keyframes、Position/Rotation/Float Error、Quaternion Continuity）
- 已生成信息与重置（Generated、Reset）

## 2.3 Animator Transition Split-Insert Tool

入口：
- `Tools/Kimodo/Animator/Split Transition And Insert Generated Motion...`
- Animator Transition 右键上下文菜单同名入口

需要写的功能：
- 从选中 Transition 自动推断源/目标状态
- 自动按 Transition 时长计算生成帧数
- 参数设置（Prompt、Diffusion Steps、Random/Seed、Model、VRAM、Output Folder）
- 可选起止姿态 JSON（Boundary Poses）
- 一键生成并插入中间状态（`Generate And Insert`）
- 取消运行中任务（`Cancel`）

## 2.4 Constraint Marker Inspector

入口：Timeline 上选中 Kimodo 约束 Marker

类型与功能：
- `Root2D`: 根轨迹（X/Z）与可选朝向约束
- `FullBody`: 全身姿态约束（根位置 + 局部关节旋转）
- `EndEffector`: 手/脚/自定义末端链约束

需要写的功能：
- `useOverride` 与自动采样模式区别
- `Frame Index (Auto from Marker)` 与 marker 时间联动
- `Edit` 按钮进入场景编辑会话
- 只读预览与可编辑模式切换逻辑

## 2.5 Constraint Override Edit Window

入口：在 Constraint Marker Inspector 点击 `Edit`

需要写的功能：
- 会话用途：在 Scene 里直接调整预览姿态并回写
- `Cancel`（放弃修改） vs `End Edit`（提交修改）
- 会话失效条件（marker 丢失、session inactive）

## 2.6 BVH Loader Inspector

入口：选中 `KimodoBVHLoader` 组件对象

需要写的功能：
- `Build Preview From BVH`
- `Play` / `Stop`
- `Clear Preview`
- 说明该工具用于导入前预览验证

## 2.7 Timeline Floating UI Overlay

入口：由 `Enable Floating UI` 开关控制（Server Manager）

需要写的功能：
- 作用窗口范围（Timeline/Animator）
- 打开/关闭方式
- 使用场景（快速输入 prompt、操作辅助）

---

## 3. 建议 Manual 章节结构

1. 安装与首次启动  
2. Server Manager 配置  
3. Timeline 生成流程（KimodoPlayableClip）  
4. 约束系统（Marker + Override Edit）  
5. Animator Transition 自动插入流程  
6. BVH 预览导入流程  
7. 常见问题与排查（服务未启动、模型缺失、编译中操作被排队等）  

---

## 4. 最小上手流程（建议放在 Manual 首页）

1. 打开 `Project Settings > Kimodo Server Manager`，点击 `Create Kimodo Server`（如需要）。  
2. 选择 `Model` 与 `VRAM Mode`，启动服务。  
3. 在 Timeline 选中 `KimodoPlayableClip`，填写 Prompt 和时长，点击 `Generate & Bake`。  
4. 如需精确控制，在 clip 对应帧添加 Constraint Marker 并用 `Edit` 进行姿态约束。  
5. 如需改造 Animator 状态机过渡，选中 Transition 后使用 Split-Insert 工具自动生成并插入过渡动作。  

