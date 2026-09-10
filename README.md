# SkillSystem Casting

> **包名**：`com.techcosmos.skillsystem.casting`  
> **版本**：**2.0.0**  
> **依赖**：`com.techcosmos.skillsystem`（SkillSystem Runtime 3.3+，需 `INestedEffectEntryOwner`）  
> **Unity**：2022.3 或更高  
> **命名空间**：`TechCosmos.SkillSystem.Casting`

技能框架的**可选施法扩展**：只做**释放前的读条**和这段读条的打断。  
**引导不是控制器阶段。** 出手之后的持续由本包 `ChannelMechanism`（机制 + Buff）管理。SkillSystem 本体不包含引导。

**核心等式**：

```
TryCast（核心 API 不变）
  → 守卫 / 目标类型（核心 SkillHolder）
  → SkillHolder.Executor.TryExecute（本包 SkillExecutionController）
       有前摇 → 读条 → 再走 SkillExecutionPipeline
       时长为 0 → 立刻 Pipeline.Execute
         若机制树里有 ChannelMechanism → 打上引导 Buff，之后由 Buff 跳
```

---

## 目录

1. [这是什么、不管什么](#1-这是什么不管什么)
2. [安装](#2-安装)
3. [5 分钟接入](#3-5-分钟接入)
4. [施法流程](#4-施法流程)
5. [数值层键](#5-数值层键)
6. [打断与优先级](#6-打断与优先级)
7. [事件与表现](#7-事件与表现)
8. [API 速查](#8-api-速查)
9. [FAQ](#9-faq)

---

## 1. 这是什么、不管什么

### 管

- 读条（`SkillCastTime`）
- 前摇可否被外部打断
- 读完后调用核心 `SkillExecutionPipeline`
- 把前摇键灌进技能数值层（中间件 + 技能编辑器「概念」分组）
- 前摇之外：`TryInterrupt` / `IsBusy` 会问 `ChannelSessionService`（引导 Buff 还在不在）

### 不管（项目自己写 / 机制层做）

| 事项 | 为什么 |
|------|--------|
| 引导持续、脉冲、成功/失败 | `ChannelMechanism` + `BuffDataSO` |
| 每帧 `Tick()` | 时钟在你的 Update / ECS System 里（前摇要 Tick；引导靠 BuffUpdate） |
| 走了要不要打断 | 这是玩法规则 |
| 读条要不要站桩 | 导航 / 移动组件是你的 |
| 播什么动画、Cue | 订本包事件 + `ChannelSessionService.IsActive` |
| `IUnit` 是哪个类 | 本包菜单弹框选，生成封闭中间件 |

不挂本包时：`SkillHolder.TryCast` 守卫通过后立刻 `ExecuteLayer.Execute`，和没装扩展一样。

---

## 2. 安装

1. 工程里已有 SkillSystem Runtime（asmdef：`TechCosmos.SkillSystem.Runtime`）。
2. 把本包放到 `Packages/` 或 `Assets/` 下。
3. 项目程序集引用 `TechCosmos.SkillSystem.Casting`（`autoReferenced: true` 时默认能看到）。

本包 **不** 引用你的 `Unit` 类型。中间件是开泛型；用本包菜单选一个项目里的 `IUnit` 生成封闭类。 **不要** 用技能框架的 Generate All 生成本包中间件。

---

## 3. 5 分钟接入

### 3.1 生成中间件封闭类

菜单：`Tech-Cosmos → SkillSystem Casting → 生成 Casting 封闭类`。  
一次生成前摇中间件和引导机制的封闭类（`Generated/Casting`）。

数值层出现（中间件）：

| 键 | 类型 | 默认 | 含义 |
|----|------|------|------|
| `SkillCastTime` | float / 公式 | `0` | 前摇（秒） |
| `SkillCastCanBeInterrupted` | bool | `true` | 前摇能否被外部打断 |

引导请在机制树加 **引导**（`ChannelMechanism`），不要再找 `SkillChannelTime`。

### 3.2 把控制器挂到 SkillHolder.Executor

每个施法者一个控制器实例，并且 **Tick 的必须是这同一个实例**。

```csharp
_skills.Executor = _cast;
```

对外仍是核心的 `TryCast`。有前摇时返回 `true` 表示**已经进入读条**，不是机制已经结算。

### 3.3 每帧 Tick

不 Tick，前摇永远走不完。引导不靠这个 Tick，靠单位的 `BuffSystem.BuffUpdate`。

---

## 4. 施法流程

```
SkillHolder.TryCast
  └─ Executor.TryExecute
        ├─ 正在忙（前摇或引导）且不能被本技能顶掉 → false
        ├─ castTime>0
        │     ├─ Pipeline.CanExecute 失败 → false（不开读条）
        │     └─ Phase = Casting → Tick 攒满 → CompleteCast → Pipeline.Execute
        └─ castTime=0 → 立刻 Pipeline.Execute
```

`CanExecute` 与 `Execute` 前半段对齐。  
**事件触发的主动技能**仍走核心立刻 Execute，**不**经过本包。

---

## 5. 数值层键

```csharp
SkillCastTiming.CastTimeKey                 // "SkillCastTime"
SkillCastTiming.CastCanBeInterruptedKey     // "SkillCastCanBeInterrupted"
SkillCastTiming.CanBeInterruptedKey         // "SkillCanBeInterrupted"（旧键，缺新键时回退）
```

缺键时：时长当 `0`，可打断当 `true`。  
编辑器第一次写入 `SkillCastCanBeInterrupted` 时，若已有旧键会抄旧值。

引导键在机制上：`ChannelDuration` / `ChannelCanBeInterrupted` / `ChannelBuffId`。

---

## 6. 打断与优先级

### 6.1 TryInterrupt / Cancel

```csharp
_cast.TryInterrupt(InterruptReason.Movement);
_cast.Cancel();   // = Manual
```

有前摇：按前摇可打断键。`Manual` / `Death` 总能断前摇。  
没有前摇、但该施法者正在引导：转到 `ChannelSessionService.TryInterrupt`。  
项目不调用 `TryInterrupt`，读条/引导就不会因移动/受伤而断。

### 6.2 用更高优先级顶掉

正在读条或引导时再 `TryCast`：

- 当前不可打断 → 新技能失败
- 当前可打断且新技能优先级 **大于** 当前 → 旧的按 `Manual` 打断，开始新技能
- 否则 → 新技能失败

不可打断 = 不能被外部打断，也不能被更高优先级顶掉。  
例外：`TryInterrupt(Manual)` 和 `TryInterrupt(Death)`。死亡后立刻放技能：先 `TryInterrupt(Death)`，再 `TryCast`。

### 6.3 忙状态

```csharp
bool busy = _cast.IsBusy;   // 前摇 或 ChannelSessionService.IsActive
SkillCastPhase phase = _cast.Phase;   // 只有 None / Casting；Channeling / Executing 预留
```

`LastCastElapsed`：上次成功出手时前摇走了多久。引导已过请读 `ChannelSessionService.GetElapsed` / `GetLastElapsed`。

---

## 7. 事件与表现

```csharp
_cast.OnCastStarted     += (skill, ctx) => { /* 读条开始 */ };
_cast.OnCastCompleted   += (skill, ctx) => { /* 管线成功；引导可能此刻才开始 */ };
_cast.OnCastFailed      += (skill, ctx, result) => { /* 条走完但结算失败 */ };
_cast.OnCastInterrupted += (skill, info) => { /* 前摇打断，或转发的引导打断 */ };
```

没有前摇、直接引导的技能：**不会** `OnCastStarted`。请用 `ChannelSessionService.IsActive(caster)` 切引导姿态。

---

## 8. API 速查

| 类型 | 作用 |
|------|------|
| `SkillExecutionController<T>` | 前摇状态机；`IsBusy` 含引导占用 |
| `SkillCastPhase` | `None` / `Casting`；`Channeling`/`Executing` 预留 |
| `InterruptReason` | 打断原因 |
| `SkillCastTiming` | 前摇键 |
| `CastInterruptInfo` | 打断时的原因、阶段、已过时间 |
| `SkillCastMiddleware<T>` | 灌前摇键 |

引导：本包 `ChannelMechanism<T>` / `ChannelSessionService`。封闭类用本包菜单和前摇中间件一起生成。

---

## 9. FAQ

**装了包但所有技能都是秒放？**  
没挂 `Executor`、没 Tick、或 `SkillCastTime` 是 0。

**数值层没有前摇键？**  
没走本包菜单「生成 Casting 封闭类」。

**引导去哪了？**  
机制树加「引导」。持续写 `BuffDataSO`，开始/成功/失败挂叶子。不要再用 `SkillChannelTime`。

**TryCast 返回 true 但伤害还没出？**  
有前摇时 `true` 只表示进了 Casting。结算在 Tick 走完之后。引导伤害应写在引导 Buff 或成功/失败钩子，不要挂在引导节点后面的兄弟机制上指望「等引导结束」。

**公式里按引导时间算？**  
读 `ChannelSessionService.GetLastElapsed`。管线跑的时候引导才刚开始，`GetElapsed` 几乎是 0。
