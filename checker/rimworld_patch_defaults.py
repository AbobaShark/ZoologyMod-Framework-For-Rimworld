from decimal import Decimal, InvalidOperation


STAT_BASE_DEFAULTS = {
    "MarketValue": "1",
    "MoveSpeed": "3",
    "Wildness": "-1",
    "FilthRate": "1",
    "ComfyTemperatureMin": "0",
    "ComfyTemperatureMax": "40",
    "ArmorRating_Blunt": "0",
    "ArmorRating_Sharp": "0",
    "ArmorRating_Heat": "0",
    "ToxicEnvironmentResistance": "0",
    "LeatherAmount": "0",
}


RACE_FIELD_DEFAULTS = {
    "hasGenders": "true",
    "needsRest": "true",
    "foodType": "",
    "wildBiomes": None,
    "executionRange": "2",
    "lifeExpectancy": "10",
    "roamMtbDays": None,
    "allowedOnCaravan": "true",
    "canReleaseToWild": "true",
    "playerCanChangeMaster": "true",
    "showTrainables": "true",
    "hideTrainingTab": "false",
    "doesntMove": "false",
    "canOpenFactionlessDoors": "true",
    "alwaysAwake": "false",
    "alwaysViolent": "false",
    "isImmuneToInfections": "false",
    "bleedRateFactor": "1",
    "canBecomeShambler": "false",
    "neverIncludeInQuests": "false",
    "canBeVacuumBurnt": "true",
    "herdAnimal": "false",
    "packAnimal": "false",
    "predator": "false",
    "maxPreyBodySize": "99999",
    "petness": "0",
    "nuzzleMtbHours": "-1",
    "manhunterOnDamageChance": "0",
    "manhunterOnTameFailChance": "0",
    "canBePredatorPrey": "true",
    "herdMigrationAllowed": "true",
    "waterSeeker": "false",
    "waterCellCost": None,
    "disableMating": "false",
    "canFishForFood": "false",
    "canFlyInVacuum": "false",
    "flightStartChanceOnJobStart": "0",
    "flightSpeedFactor": "2.8",
    "canFlyIntoMap": "false",
    "canLeaveMapFlying": "false",
    "leaveMapOnFleeChance": "0",
    "maxMechEnergy": "100",
    "mechFixedSkillLevel": "10",
    "gestationPeriodDays": "-1",
    "litterSizeCurve": None,
    "mateMtbHours": "12",
    "trainability": None,
    "specialTrainables": None,
    "nameOnTameChance": "0",
    "baseBodySize": "1",
    "baseHealthScale": "1",
    "baseHungerRate": "1",
    "hasMeat": "true",
    "meatMarketValue": "2",
    "useMeatFrom": None,
    "useLeatherFrom": None,
    "hasCorpse": "true",
    "hasUnnaturalCorpse": "false",
    "corpseHiddenWhileUndiscovered": "false",
    "leatherDef": None,
    "soundCallIntervalFriendlyFactor": "1",
    "soundCallIntervalAggressiveFactor": "0.25",
    "anomalyKnowledge": "0",
}


PAWN_KIND_FIELD_DEFAULTS = {
    "moveSpeedFactorByTerrainTag": None,
    "combatPower": "-1",
    "canArriveManhunter": "true",
    "wildGroupSize": "1",
    "ecoSystemWeight": "1",
}


def normalize_scalar(value):
    if value is None:
        return None
    text = str(value).strip()
    lowered = text.lower()
    if lowered in ("true", "false"):
        return lowered
    try:
        dec = Decimal(text.replace(",", "."))
    except (InvalidOperation, ValueError):
        return " ".join(text.split())
    normalized = format(dec.normalize(), "f")
    if "." in normalized:
        normalized = normalized.rstrip("0").rstrip(".")
    if normalized == "-0":
        normalized = "0"
    return normalized


def scalar_values_equal(left, right):
    return normalize_scalar(left) == normalize_scalar(right)

