# SkillSystem Casting

> **包名**：`com.techcosmos.skillsystem.casting`  
> **版本**：**1.4.0**  
> **依赖**：`com.techcosmos.skillsystem`（SkillSystem Runtime 3.2+，需带 `RequiredData.SeedFromKey`）  
> **Unity**：2022.3 或更高  
> **命名空间**：`TechCosmos.SkillSystem.Casting`

技能框架的**可选施法扩展**：给主动技能加上前摇、引导和打断。  
核心包只有「立刻结算」；本包把「带时间的施法」补上，**不接管**单位循环、移动、AI、表现。

**核心等式**：

```
TryCast（核心 API 不变）
  → 守卫 / 目标类型（核心 SkillHolder）
  → SkillHolder.Executor.TryExecute（本包 SkillExecutionController）
       有前摇/引导 → 读条 / 引导 → 再走 SkillExecutionPipeline
       时长为 0     → 立刻 Pipeline.Execute
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
- 引导（`SkillChannelTime`）
- 前摇 / 引导可否被外部打断（分阶段两个键）
- 读完后调用核心 `SkillExecutionPipeline`
- 把时长和可打断键灌进技能数值层（中间件 + 技能编辑器「概念」分组）

### 不管（项目自己写）

| 事项 | 为什么 |
|------|--------|
| 每帧 `Tick()` | 时钟在你的 Update / ECS System 里 |
| 走了要不要打断 | 这是玩法规则 |
| 读条要不要站桩 | 导航 / 移动组件是你的 |
| 播什么动画、Cue | 订本包事件即可，Clip 在你这边 |
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

三件必须做的事。少一件，读条不会发生或时长键不会出现。

### 3.1 生成中间件封闭类

菜单：`Tech-Cosmos → SkillSystem Casting → Generate Cast Middleware`。

弹出项目里实现了 `IUnit<>` 的类型列表，选一个，点 **生成**。  
得到 `YourUnitSkillCastMiddleware`（目录 `Assets/Generated/Casting/`，避免被核心 Generate All 清掉），技能数值层出现：

| 键 | 类型 | 默认 | 含义 |
|----|------|------|------|
| `SkillCastTime` | float / 公式 | `0` | 前摇（秒） |
| `SkillChannelTime` | float / 公式 | `0` | 引导（秒） |
| `SkillCastCanBeInterrupted` | bool | `true` | 前摇能否被外部打断 |
| `SkillChannelCanBeInterrupted` | bool | `true` | 引导能否被外部打断 |

把生成的中间件挂进你的 `GlobalMiddlewareRegistry`（和其他中间件一样）。  
两个时长都是 `0` 的技能仍然立刻放，行为与没装本包相同。

### 3.2 把控制器挂到 SkillHolder.Executor

每个施法者一个控制器实例，并且 **Tick 的必须是这同一个实例**。

```csharp
using TechCosmos.SkillSystem.Casting;
using TechCosmos.SkillSystem.Runtime;

public class Hero : MonoBehaviour, IUnit<Hero>
{
    SkillHolder<Hero> _skills;
    SkillExecutionController<Hero> _cast;

    void Awake()
    {
        _skills = new SkillHolder<Hero>(/* 你的 UnitEvent */);
        _cast = new SkillExecutionController<Hero>();
        _skills.Executor = _cast;   // 同一实例
    }

    public bool TryCast(ISkill<Hero> skill, SkillContext<Hero> context)
        => _skills.TryCast(skill, context);

    public bool TryCast(SkillId skillId, SkillContext<Hero> context)
        => _skills.TryCast(skillId, context);
}
```

对外仍是核心的 `TryCast`。有前摇时返回 `true` 表示**已经进入读条**，不是机制已经结算。

### 3.3 每帧 Tick

不 Tick，前摇永远走不完。

```csharp
void Update()
{
    _cast.Tick();   // 或在 ECS System 里对每个单位 Tick
}
```

时钟默认 `SkillSystemServices.Clock`。单测可注入 `ISkillClock`：

```csharp
var controller = new SkillExecutionController<Hero>(clock);
```

---

## 4. 施法流程

```
SkillHolder.TryCast
  ├─ 不是主动技能 → false（打日志）
  ├─ SkillCastValidator（目标 / 点地 / 无目标）
  ├─ ICastGuard
  └─ Executor.TryExecute        ← 本包
        ├─ 正在忙且不能被本技能顶掉 → false
        ├─ castTime>0 或 channelTime>0
        │     ├─ Pipeline.CanExecute 失败 → false（不开读条）
        │     └─ Phase = Casting，派发 OnCastStarted
        │           Tick 攒满 castTime
        │             ├─ 有 channelTime → 记下前摇已过时间，Phase = Channeling，elapsed 清零
        │             │     Tick 攒满 channelTime → CompleteCast
        │             └─ 无引导 → CompleteCast
        │                   写入 LastCastElapsed / LastChannelElapsed，再清会话，再 Pipeline.Execute
        │                     成功 → OnCastCompleted
        │                     失败 → OnCastFailed（蓝不够、条件、中间件取消等）
        ├─ TryRelease（仅引导中）→ 同上 CompleteCast
        └─ 两个时长都是 0 → 立刻 Pipeline.Execute，返回是否 Success
```

`CanExecute` 与 `Execute` 前半段对齐：中间件 `OnBeforeExecute` + ConfirmCast。  
读条开始前失败则根本不进 Casting，避免「条已经走了、结算却没有」。

**事件触发的主动技能**（`ActiveBaseLayer.Trigger`）仍走核心立刻 Execute，**不**经过本包。玩家/AI 请走 `TryCast`。

---

## 5. 数值层键

控制器只认这些名字（`SkillCastTiming` 常量，与中间件 `RequiredData` 一致）：

```csharp
SkillCastTiming.CastTimeKey           // "SkillCastTime"
SkillCastTiming.ChannelTimeKey        // "SkillChannelTime"
SkillCastTiming.CastCanBeInterruptedKey     // "SkillCastCanBeInterrupted"
SkillCastTiming.ChannelCanBeInterruptedKey  // "SkillChannelCanBeInterrupted"
SkillCastTiming.CanBeInterruptedKey         // "SkillCanBeInterrupted"（旧键，缺新键时回退）
```

读取：

```csharp
float windup = SkillCastTiming.GetCastTime(skill, context);
float channel = SkillCastTiming.GetChannelTime(skill, context);
bool castOk = SkillCastTiming.GetCastCanBeInterrupted(skill, context);
bool channelOk = SkillCastTiming.GetChannelCanBeInterrupted(skill, context);
```

缺键时：时长当 `0`，可打断当 `true`。  
编辑器**第一次写入**两个新打断键时，若数值层已有旧键 `SkillCanBeInterrupted`，会抄旧值，不走默认 `true`。已经生成过新键的资源不会自动改。  
运行时：新键在就用新键，缺新键才回退旧键。  
技能编辑器里改这些数即可，**不必**为本包再开编辑窗口。

---

## 6. 打断与优先级

### 6.1 TryInterrupt / Cancel

```csharp
_cast.TryInterrupt(InterruptReason.Movement);
_cast.TryInterrupt(InterruptReason.Death);
_cast.Cancel();   // = Manual
```

| 原因 | 何时由项目调用（示例，不是包内逻辑） |
|------|--------------------------------------|
| `Manual` | 玩家停止、切技能取消 |
| `Movement` | 开始移动、寻路被破坏 |
| `Damage` | 挨打要断读条时 |
| `HardCrowdControl` | 眩晕等 |
| `Silence` | 沉默 |
| `Death` | 死亡、回池 |

当前阶段对应的可打断为 `false` 时：只有 **`Manual` 和 `Death`** 能打断，其它原因返回 `false`、该阶段继续。  
缺新键时回退旧键 `SkillCanBeInterrupted`（两阶段同一值）。

项目不调用 `TryInterrupt`，读条就不会因移动/受伤而断。这是刻意的。

### 6.2 用更高优先级顶掉当前读条

`SkillProfile.executionPriority` 仍在核心 Profile 上。  
正在读条时再 `TryCast` 另一个技能：

- 当前阶段不可打断 → 新技能失败，旧读条继续（优先级再高也顶不掉）
- 当前可打断且新技能优先级 **大于** 当前 → 旧的按 `Manual` 打断，开始新读条
- 否则 → 新技能失败

不可打断 = 当前阶段既不能被外部打断，也不能被更高优先级顶掉。  
例外：`TryInterrupt(Manual)` 和 `TryInterrupt(Death)` 仍然能断。死亡后立刻放技能：先 `TryInterrupt(Death)`，再 `TryCast`。

### 6.3 忙状态

```csharp
bool busy = _cast.IsBusy;
SkillCastPhase phase = _cast.Phase;   // None / Casting / Channeling；Executing 预留，同步管线不会进
ISkill<Hero> current = _cast.ActiveSkill;
```

AI、指令、站桩都可以读这些。本包不替你停 NavMesh。

```csharp
float windup = _cast.LastCastElapsed;     // 上次出手时前摇走了多久
float charged = _cast.LastChannelElapsed; // 上次出手时引导走了多久
```

管线跑的时候 `Elapsed` 已经是 0。打断不改这两份。只有前摇、没有引导时，`LastChannelElapsed` 为 0。

### 6.4 提前释放

```csharp
bool released = _cast.TryRelease();
```

**只在引导中**立刻结算，不是 `Cancel`。前摇是硬门槛，前摇中或空闲返回 `false`。  
不看可打断键。要做充能提前出手，数值层得给 `SkillChannelTime`。

项目自己决定谁按键、要不要最短充能。本包不替你绑输入。

---

## 7. 事件与表现

```csharp
_cast.OnCastStarted     += (skill, ctx) => { /* 读条开始：播前摇动画 */ };
_cast.OnCastCompleted   += (skill, ctx) => { /* 管线成功 */ };
_cast.OnCastFailed      += (skill, ctx, result) => { /* 条走完但结算失败 */ };
_cast.OnCastInterrupted += (skill, info) => { /* info.Phase / info.Elapsed / info.CastElapsed / info.TotalElapsed / info.Reason */ };
```

核心 `SkillPresentationBinder.BindCaster` 订的是 ExecuteLayer（机制开跑 ≈ CastStart）。  
读条真正开始请订 **本包** `OnCastStarted`，不要指望 Binder 的 CastStart 等于开读条。

---

## 8. API 速查

| 类型 | 作用 |
|------|------|
| `SkillExecutionController<T>` | 状态机；`LastCastElapsed` / `LastChannelElapsed` / `TryRelease` |
| `SkillCastPhase` | `None` / `Casting` / `Channeling`；`Executing` 预留，当前不会进入 |
| `InterruptReason` | 打断原因枚举 |
| `SkillCastTiming` | 时长 / 分阶段可打断键 + 从 DataLayer 读取 |
| `CastInterruptInfo` | 打断时的原因、阶段、本阶段已过、前摇已过、合计已过 |
| `SkillCastMiddleware<T>` | 开泛型中间件；本包菜单选 IUnit 生成封闭类 |

`SkillHolder<T>.Executor`（核心）：

```csharp
holder.Executor = controller;
holder.TryCast(skill, context);
```

---

## 9. FAQ

**装了包但所有技能都是秒放？**  
没挂 `Executor`、没 Tick、或 `SkillCastTime` / `SkillChannelTime` 都是 0。先查这三件。

**数值层没有时长 / 可打断键？**  
没走本包菜单 `Generate Cast Middleware`、没选 IUnit，或生成类没进 Middleware Registry。技能框架的 Generate All **不会**生成本包中间件。

**TryCast 返回 true 但伤害还没出？**  
有前摇时 `true` 只表示进了 Casting。结算在 Tick 走完或 `TryRelease` 之后。

**公式里按充能时间算伤害？**  
读 `LastChannelElapsed`，不要读 `Elapsed`。`SkillChannelTime` 是引导上限，`SkillCastTime` 是必须走满的前摇。

**事件被动/主动 Trigger 没有前摇？**  
`ActiveBaseLayer.Trigger` 不走 Executor。指令施法请 `TryCast`。

**和 ICastGuard 什么关系？**  
守卫在 `TryCast` 入口、Executor **之前**。眩晕禁施法用守卫拦新技能；已经在读条的要用 `TryInterrupt`。

**能改键名吗？**  
控制器写死上述三个常量。要换名字只能改本包或自己实现 `ISkillExecutor`。

**UnitBase 会自动 Tick 吗？**  
不会。核心 `UnitBase` 不再创建控制器。自己持有 `SkillHolder` 的单位按第 3 节接。
