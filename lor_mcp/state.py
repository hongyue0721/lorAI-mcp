"""Game state models for Library of Ruina."""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


# ---------- Dice & Buff ----------

class LorSpeedDice(BaseModel):
    """速度骰子"""

    value: int
    breaked: bool = Field(description="骰子是否被破坏(Break)")
    is_controlable: bool = Field(description="骰子是否可被操控")


class LorDiceInfo(BaseModel):
    """骰子行为信息"""

    type: str = Field(description="骰子类型: Atk/Def/ETC 等")
    value_min: int
    value_max: int
    coin_count: int = Field(description="硬币数量")
    behavior_id: str = Field(description="行为ID")


class LorBuff(BaseModel):
    """Buff/Debuff 状态"""

    name: str
    stack: int
    remain_time: int = Field(description="剩余回合数, -1 表示永久")
    positive_type: str = Field(description="正面/负面类型标记")


# ---------- Card ----------

class LorCard(BaseModel):
    """战斗页(Combat Page / 卡牌)"""

    id: str = Field(description="LorId 格式的卡牌ID")
    name: str
    cost: int = Field(description="费用/光芒消耗")
    dice_list: list[LorDiceInfo] = Field(default_factory=list, description="骰子行为列表")


class LorCardSlotInfo(BaseModel):
    """卡牌槽位(拼点时的卡牌放置信息)"""

    speed_dice_idx: int = Field(description="对应的速度骰子索引")
    card: LorCard | None = Field(default=None, description="放置的卡牌, None 表示空槽")
    target_unit_idx: int | None = Field(default=None, description="目标单位索引")
    target_speed_dice_idx: int | None = Field(default=None, description="目标速度骰子索引")


# ---------- Unit ----------

class LorUnit(BaseModel):
    """战斗单位(司书/客人)"""

    id: int
    index: int
    faction: str = Field(description="阵营: Player(司书) / Enemy(客人)")
    name: str
    hp: float
    max_hp: int
    break_life: int = Field(description="混乱/Break 生命值")
    stagger: bool = Field(description="是否处于混乱(Stagger)状态")
    dead: bool
    speed_dice: list[LorSpeedDice] = Field(default_factory=list)
    play_point: int = Field(description="当前光芒(Light/Play Point)")
    max_play_point: int = Field(description="最大光芒")
    hand_cards: list[LorCard] = Field(default_factory=list, description="手牌")
    equipped_cards: list[LorCardSlotInfo] = Field(default_factory=list, description="已配置的卡牌槽位")
    buffs: list[LorBuff] = Field(default_factory=list)
    passives: list[str] = Field(default_factory=list, description="被动能力名称列表")
    emotion_level: int = Field(description="情感等级")
    emotion_coins: dict[str, Any] = Field(default_factory=dict, description="情感硬币信息")


# ---------- Team ----------

class LorTeam(BaseModel):
    """一方阵营信息"""

    units: list[LorUnit] = Field(default_factory=list)


# ---------- Top-level State ----------

class LorState(BaseModel):
    """游戏总状态"""

    phase: str = Field(description="当前阶段 (StagePhase 枚举值)")
    round_turn: int = Field(description="回合数")
    current_wave: int = Field(description="当前波次")
    current_floor: str = Field(description="当前楼层 (SephirahType)")
    librarian_team: LorTeam = Field(description="司书团队")
    enemy_team: LorTeam = Field(description="客人(敌人)团队")
    units: list[LorUnit] = Field(default_factory=list, description="所有单位的平坦列表")