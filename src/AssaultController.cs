﻿using HarmonyLib;
using rail;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DSP_Battle
{
    public static class AssaultController
    {
        // 存档内容
        public static bool voidInvasionEnabled = true; // 入侵逻辑是否已开启
        public static int difficulty = 2;

        public static List<AssaultHive> assaultHives = new List<AssaultHive>(); // 虚空入侵已激活的hive
        public static bool modifierEnabled; // 是否已启用入侵修改器
        public static List<int> modifier; // 入侵修改器（附带效果）

        public static bool timeChangedByRelic = false;

        // 不需要存档
        public static int[] invincibleHives; // 因为虚空入侵处于几乎无敌（对恒星炮除外）的hive
        public static int[] modifierHives; // 因为虚空入侵获得了修改器的hive
        public static int[] alertHives; // 因为虚空入侵需要更改警告显示的hive
        public static bool assaultActive; // 入侵已实例化，正在入侵的某个阶段中

        // UIDarkFogMonitor里面的OrganizeTargetList决定显示在左上角的警报和顺序

        //public static bool quickBuild0 = false; // 枪骑
        //public static bool quickBuild1 = false;
        //public static bool quickBuild2 = false;

        //public static bool quickBuildNode = false;

        public static int quickTickHive = -1;
        public static int quickTickFactor = 1;

        public static int testLvlSet = -1;

        public static List<int> oriAstroId = new List<int>();
        public static List<int> time = new List<int>();
        public static List<int> state = new List<int>();
        public static List<int> level = new List<int>();
        public static int count = 0;

        // modifier index
        public const int DamageReduction = 0; // 独立减伤百分比
        public const int Dodge = 1; // 独立闪避百分比
        public const int DamageToShield = 2; // 对行星护盾增伤
        public const int DropletInvincible = 3; // 免疫水滴伤害
        public const int AddtionalArmor = 4; // 额外护甲
        public const int SpeedUp = 5; // 移速加成百分比
        public const int HardenedStructure = 6; // 刚毅结构（超出该数值的伤害会被降低为该数值）


        public static void InitWhenLoad()
        {
            voidInvasionEnabled = false;
            difficulty = 2;
            assaultHives = new List<AssaultHive>();
            invincibleHives = new int[GameMain.spaceSector.maxHiveCount];
            modifierHives = new int[GameMain.spaceSector.maxHiveCount];
            alertHives = new int[GameMain.spaceSector.maxHiveCount];
            modifier = new List<int>(50);
            timeChangedByRelic = false;
            ClearDataArrays();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameData), "GameTick")]
        public static void LogicTick(long time)
        {
            MoreMegaStructure.MMSCPU.BeginSample(TCFVPerformanceMonitor.MainLogic);
            MoreMegaStructure.MMSCPU.BeginSample(TCFVPerformanceMonitor.Assault);

            // 一定条件下，开启新入侵
            if (voidInvasionEnabled && assaultHives.Count == 0 && time % 3600 == 0)
            {
                int starIndex = -1;
                bool haveStarEnergyUsing = false;
                if (GameMain.data.dysonSpheres != null)
                {
                    for (int i = 0; i < GameMain.data.dysonSpheres.Length; i++)
                    {
                        if (GameMain.data.dysonSpheres[i] != null)
                        {
                            if (GameMain.data.dysonSpheres[i].energyGenCurrentTick_Swarm > 0)
                            {
                                haveStarEnergyUsing = true;
                                starIndex = GameMain.data.dysonSpheres[i].starData.index;
                                break;
                            }
                        }
                    }
                }
                if(haveStarEnergyUsing)
                    InitNewAssault(-1);
            }

            for (int i = 0; i < assaultHives.Count; i++)
            {
                assaultHives[i].LogicTick();
            }
            CalcCombatState();
            PostLogicTick(time);
            MoreMegaStructure.MMSCPU.EndSample(TCFVPerformanceMonitor.Assault);
            MoreMegaStructure.MMSCPU.EndSample(TCFVPerformanceMonitor.MainLogic);
        }

        /// <summary>
        /// 从assaultHives获取hive信息，存储在字典中方便调用
        /// </summary>
        /// <param name="time"></param>
        public static void PostLogicTick(long time)
        {
            bool removeAll = assaultHives != null && assaultHives.Count > 0;
            for (int i = 0; i < assaultHives.Count; i++)
            {
                if(assaultHives[i].state == EAssaultHiveState.Remove)
                {
                    invincibleHives[assaultHives[i].byAstroIndex] = -1;
                    modifierHives[assaultHives[i].byAstroIndex] = -1;
                    alertHives[assaultHives[i].byAstroIndex] = -1;
                }
                else
                {
                    removeAll = false;
                }
            }
            if(removeAll)
            {
                OnAssaultEnd();
            }
        }

        public static void CalcCombatState()
        {
            if(assaultHives == null || assaultHives.Count == 0)
            {
                Configs.combatState = 0;
            }
            else
            {
                bool generated = false;
                bool warning = false;
                bool inCombat = false;
                for (int i = 0; i < assaultHives.Count; i++)
                {
                    if (assaultHives[i].state == EAssaultHiveState.Assault)
                    {
                        inCombat = true;
                        break;
                    }
                    else if (assaultHives[i].timeTillAssault <= 3600 * 5)
                    {
                        warning = true;
                    }
                    else if (assaultHives[i].state != EAssaultHiveState.End && assaultHives[i].state != EAssaultHiveState.Remove)
                    {
                        generated = true;
                    }
                }
                if (inCombat)
                    Configs.combatState = 3;
                else if (warning)
                    Configs.combatState = 2;
                else if (generated)
                    Configs.combatState = 1;
            }
        }

        public static void ClearDataArrays()
        {
            if (invincibleHives == null)
            {
                invincibleHives = new int[GameMain.spaceSector.maxHiveCount];
            }
            else
            {
                for (int i = 0; i < invincibleHives.Length; i++)
                {
                    invincibleHives[i] = -1;
                }
            }

            if(modifierHives == null)
            {
                modifierHives = new int[GameMain.spaceSector.maxHiveCount];
            }
            else
            {
                for (int i = 0; i < modifierHives.Length; i++)
                {
                    modifierHives[i] = -1;
                }
            }

            if (alertHives == null)
            {
                alertHives = new int[GameMain.spaceSector.maxHiveCount];
            }
            else
            {
                for (int i = 0; i < alertHives.Length; i++)
                {
                    alertHives[i] = -1;
                }
            }

            if(modifier == null)
            {
                modifier = new List<int> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            }
            else
            {
                for (int i = 0; i < modifier.Count; i++)
                {
                    modifier[i] = 0;
                }
            }
            modifierEnabled = false;
            assaultHives.Clear();
        }

        public static void InitNewAssault(int starIndex = -1)
        {
            // 如果starIndex 为负 根据能量水平 选择一个恒星系
            if (starIndex < 0)
            {
                long energyThreshold = (long)(SkillPoints.skillLevelR[8] * 1000000L * SkillPoints.skillValuesR[8]);
                List<long> totalEnergyServedByStar = new List<long>();
                for (int i = 0; i < GameMain.galaxy.starCount; i++)
                {
                    totalEnergyServedByStar.Add(0);
                    if (GameMain.galaxy.stars[i] != null)
                    {
                        PlanetData[] planets = GameMain.galaxy.stars[i].planets;
                        for (int j = 0; j < GameMain.galaxy.stars[i].planetCount; j++)
                        {
                            if (planets[j] != null && planets[j].factory != null)
                            {
                                PlanetFactory factory = planets[j].factory;
                                PowerSystem powerSystem = factory.powerSystem;
                                if (powerSystem != null)
                                {
                                    for (int k = 1; k < powerSystem.netCursor; k++)
                                    {
                                        PowerNetwork net = powerSystem.netPool[k];
                                        if (net != null)
                                            totalEnergyServedByStar[i] += net.energyServed;
                                    }
                                }
                            }
                        }
                        if (totalEnergyServedByStar[i] * 60 <= energyThreshold)
                            totalEnergyServedByStar[i] = 0;
                    }
                }
                long totalEnergySum = totalEnergyServedByStar.Sum();
                if (totalEnergySum <= 0)
                    totalEnergySum = 1;
                List<double> prob = new List<double>();
                if (totalEnergyServedByStar.Count > 1)
                    prob.Add(totalEnergyServedByStar[0] * 1.0 / totalEnergySum);
                for (int i = 1; i < totalEnergyServedByStar.Count; i++)
                {
                    prob.Add(prob[i - 1] + (totalEnergyServedByStar[i] * 1.0 / totalEnergySum));
                }

                double randSeed = Utils.RandDouble();
                for (int i = 0; i < prob.Count; i++)
                {
                    if (prob[i] >= randSeed)
                    {
                        starIndex = i;
                        break;
                    }
                }
            }
            ClearDataArrays();
            if (starIndex < 0)
                return;

            int waveCount = Configs.wavePerStar[starIndex];
            int totalNum = GetAssaultTotalNum(waveCount);
            int minHiveCount = totalNum / 1440 + 1;
            int maxHiveCount = Math.Min(totalNum / 1440 + 4, totalNum / 10 + 1) + 1;
            maxHiveCount = Math.Min(maxHiveCount, 9);
            int hiveCount = Utils.RandInt(minHiveCount, maxHiveCount);
            int eachNum = totalNum / hiveCount;
            int level = GetHiveLevel(waveCount);

            for (int i = 0; i < hiveCount; i++)
            {
                int realNum = (int)(((Utils.RandDouble() - 0.5) / 2.5 + 1) * eachNum) + 1;
                AssaultHive ah = new AssaultHive(starIndex, i, assaultHives.Count);
                ah.assaultNumTotal = realNum;
                ah.level = level;
                ah.hive.evolve.level = Math.Max(ah.hive.evolve.level, ah.level);
                ah.inhibitPointsTotalInit = ah.level * 10 + ah.assaultNumTotal * 2;
                ah.inhibitPointsLimit = ah.inhibitPointsTotalInit;
                if (i == hiveCount - 1) // 最后一个巢穴无法被完全压制掉点数
                    ah.inhibitPointsLimit = (int)(ah.inhibitPointsLimit * 0.8) + 1;
                ah.time = i * 5;
                ah.timeTillAssault = GetAssembleTime(waveCount) + 5 * i;
                ah.timeTotalInit = ah.timeTillAssault;
                assaultHives.Add(ah);
            }

            assaultActive = true;
            timeChangedByRelic = false;
            Configs.wavePerStar[starIndex]++;
            //Utils.Log($"Initing new void invasion in star index {starIndex}.");
        }


        public static void OnAssaultEnd()
        {
            assaultActive = false;
            modifierEnabled = false;

            int totalKill = 0;
            int totalAssault = 0;
            for (int i = 0; i < assaultHives.Count; i++)
            {
                totalKill += assaultHives[i].enemyKilled;
                totalAssault += assaultHives[i].assaultNum;
            }
            if (totalAssault <= 0)
                totalAssault = 1;
            float rewardFactor = totalKill * 1.0f / totalAssault;
            int realReward = GiveReward(rewardFactor);
            UIDialogPatch.ShowUIDialog("虚空入侵结束".Translate(), string.Format("虚空入侵结束提示".Translate(), totalKill, totalAssault, realReward));

            assaultHives.Clear();
            ClearDataArrays();
            Configs.combatState = 0;
            BattleBGMController.SetWaveFinished();
        }

        public static int GiveReward(float factor)
        {
            int waveCount = Configs.totalWave;
            int maxRewardSP = 0;
            if (waveCount >= Configs.rewardSPMap.Count || waveCount < 0)
                maxRewardSP = Configs.rewardSPMap.Last();
            else
                maxRewardSP = Configs.rewardSPMap[waveCount];
            if (maxRewardSP > 2 && timeChangedByRelic) // relic 4-7 修改过的进攻波次最多拿两个点数
                maxRewardSP = 2;
            int realRewardSP = (int)(factor * maxRewardSP);
            SkillPoints.totalPoints += realRewardSP;

            return realRewardSP;
        }

        public static int GetAssaultTotalNum(int waveCount)
        {
            if (waveCount >= Configs.totalAssaultNumMap.Count || waveCount < 0)
                return Configs.totalAssaultNumMap.Last();
            else
                return Configs.totalAssaultNumMap[waveCount];
        }

        public static int GetHiveLevel(int waveCount)
        {
            if (waveCount >= Configs.levelMap.Count || waveCount < 0)
                return Configs.levelMap.Last();
            else
                return Configs.levelMap[waveCount];
        }

        public static int GetAssembleTime(int waveCount)
        {
            if(Configs.totalWave > 20)
            {
                int inc = Configs.totalWave / 10;
                if (inc > 10)
                    inc = 10;
                waveCount += inc;
            }

            if(waveCount >= Configs.assembleTimeMap.Count || waveCount < 0)
                return Configs.assembleTimeMap.Last();
            else
                return Configs.assembleTimeMap[waveCount];
        }

        //[HarmonyPostfix]
        //[HarmonyPatch(typeof(DFSReplicatorComponent), "LogicTick")]
        //public static void DFSReplicatorTickPostPatch(ref DFSReplicatorComponent __instance, EnemyDFHiveSystem hive, bool isLocal)
        //{
        //    // cacancyCount max (space) {1440, 120, 6} 枪骑、巨鲸、？
        //    EnemyFormation enemyFormation = hive.forms[__instance.productFormId];
        //    if (false && enemyFormation.vacancyCount > 0 && hive.starData.index == 0 && (__instance.productFormId == 0 || __instance.productFormId == 1))
        //    {
        //        int num5 = enemyFormation.AddUnit();
        //        if (isLocal && num5 > 0)
        //        {
        //            hive.InitiateUnitDeferred(__instance.productFormId, num5, __instance.productInitialPos, __instance.productInitialRot, __instance.productInitialVel, __instance.productInitialTick);
        //        }
        //        //Utils.Log($"add unit {__instance.productFormId}");
        //    }
        //}


        public static void BuildLogicBoost()
        {
            BuildLogicGroundBoost();
            BuildLogicSpaceBoost(quickTickHive, 30);
            //Utils.Log($"hive is empty? {GameMain.spaceSector.dfHivesByAstro[1].isEmpty}");
        }

        public static void BuildLogicSpaceBoost(int oriAstroId, int setLevel = -1)
        {
            if (oriAstroId < 1000000)
                return;
            int speedFactor = quickTickFactor;
            EnemyDFHiveSystem hive = GameMain.spaceSector.GetHiveByAstroId(oriAstroId);
            if(hive.evolve.level < setLevel)
                hive.evolve.level = setLevel;
            ref AnimData[] ptr2 = ref hive.sector.enemyAnimPool;
            if (hive.realized)
            {
                int cursor2 = hive.cores.cursor;
                int cursor8 = hive.builders.cursor;
                EnemyBuilderComponent[] buffer8 = hive.builders.buffer;
                DFSCoreComponent[] buffer2 = hive.cores.buffer;
                while (speedFactor > 0)
                {
                    speedFactor--;
                    for (int i = 1; i < cursor8; i++)
                    {
                        ref EnemyBuilderComponent ptr4 = ref buffer8[i];
                        if (ptr4.id == i)
                        {
                            int enemyId = ptr4.enemyId;
                            ptr4.energy = ptr4.maxEnergy;
                            ptr4.matter = ptr4.maxMatter;
                            ptr4.LogicTick();
                            if (ptr4.state >= 3)
                            {
                                ptr4.BuildLogic_Space(hive, buffer8, hive.pbuilders);
                            }
                            if (speedFactor == 0)
                                ptr4.RefreshAnimation_Space(hive.pbuilders, ref ptr2[enemyId]);
                        }
                        else
                        {
                        }
                    }
                    //for (int j = 1; j < cursor2; j++)
                    //{
                    //    if (buffer2[j].id == j)
                    //    {
                    //        buffer2[j].LogicTick(hive);
                    //    }
                    //}
                }
            }
        }

        public static void CheckHiveStatus(int starIndex)
        {
            for (int i = 1; i < 9; i++)
            {
                EnemyDFHiveSystem hive = GameMain.spaceSector.dfHivesByAstro[starIndex * 8 + i];
                if(hive != null)
                {
                    int instId = hive.pbuilders[1].instId;
                    Utils.Log($"instId is {instId}");
                    //hive.SetForNewGame();
                }
                else
                {
                    Utils.Log($"null hive at{i}");
                }
            }
        }


        public static void BuildHiveAlreadyInited(int starIndex)
        {
            for (int h = 1; h < 9; h++)
            {
                EnemyDFHiveSystem hive = GameMain.spaceSector.dfHivesByAstro[starIndex * 8 + h];
                if (hive != null)
                {
                    ref GrowthPattern_DFSpace.Builder ptr = ref hive.pbuilders[1];
                    if (ptr.instId > 0)
                    {
                        continue;
                    }
                    int protoId = ptr.protoId;
                    EnemyProto enemyProto = LDB.enemies.Select(protoId);
                    if (enemyProto != null)
                    {
                        EnemyData enemyData = default(EnemyData);
                        enemyData.protoId = (short)protoId;
                        enemyData.modelIndex = (short)enemyProto.ModelIndex;
                        enemyData.astroId = hive.hiveAstroId;
                        enemyData.originAstroId = hive.hiveAstroId;
                        enemyData.owner = 0;
                        enemyData.port = 0;
                        enemyData.dynamic = !enemyProto.IsBuilding;
                        enemyData.isSpace = true;
                        enemyData.localized = true;
                        enemyData.stateFlags = 0;
                        enemyData.pos = ptr.pos;
                        enemyData.rot = ptr.rot;
                        GameMain.spaceSector.AddEnemyDataWithComponents(ref enemyData, hive, 1, false);
                    }
                    hive.isEmpty = false;
                }
            }
        }



        /// <summary>
        /// if hive has its core
        /// </summary>
        /// <param name="hive"></param>
        /// <returns></returns>
        public static bool hasCore(this EnemyDFHiveSystem hive)
        {
            if (hive == null)
                return false;
            else
            {
                return hive.pbuilders[1].instId > 0;
            }
        }

        public static void BuildCore(this EnemyDFHiveSystem hive)
        {
            if(hive != null)
            {
                ref GrowthPattern_DFSpace.Builder ptr = ref hive.pbuilders[1];
                if (ptr.instId > 0)
                {
                    return;
                }
                int protoId = ptr.protoId;
                EnemyProto enemyProto = LDB.enemies.Select(protoId);
                if (enemyProto != null)
                {
                    EnemyData enemyData = default(EnemyData);
                    enemyData.protoId = (short)protoId;
                    enemyData.modelIndex = (short)enemyProto.ModelIndex;
                    enemyData.astroId = hive.hiveAstroId;
                    enemyData.originAstroId = hive.hiveAstroId;
                    enemyData.owner = 0;
                    enemyData.port = 0;
                    enemyData.dynamic = !enemyProto.IsBuilding;
                    enemyData.isSpace = true;
                    enemyData.localized = true;
                    enemyData.stateFlags = 0;
                    enemyData.pos = ptr.pos;
                    enemyData.rot = ptr.rot;
                    GameMain.spaceSector.AddEnemyDataWithComponents(ref enemyData, hive, 1, false);
                }
                hive.isEmpty = false; // 靠这个才能让他可在星图界面选中、显示轨道，可以显示左上角预警
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyDFHiveSystem), "InitFormations")]
        public static bool InitDFSHivePrePatch(ref EnemyDFHiveSystem __instance)
        {
            __instance.forms = new EnemyFormation[3];
            __instance.forms[0] = new EnemyFormation();
            __instance.forms[1] = new EnemyFormation();
            __instance.forms[2] = new EnemyFormation();
            __instance.forms[0].SetPortCount(1440);
            __instance.forms[1].SetPortCount(120);
            __instance.forms[2].SetPortCount(6);
            return false;
        }


        public static void TestLaunchAssault(int starIndex, int count)
        {
            EnemyDFHiveSystem hive = null;
            EAggressiveLevel aggressiveLevel = GameMain.data.history.combatSettings.aggressiveLevel;
            EnemyDFHiveSystem[] hives = GameMain.spaceSector.dfHives;

            for (int i = 0; i < hives.Length; i++)
            {
                hive = hives[i];
                if (hive != null && hive.starData != null && hive.starData.index == starIndex)
                    break;
            }
            if (hive != null)
            {
                hive.hatredAstros.Sort();
                ref HatredTarget ptr = ref hive.hatredAstros.max;
                bool flag2 = false;
                int targetAstroId = 0;
                Vector3 tarPos = Vector3.zero;
                Vector3 maxHatredPos = Vector3.zero;
                for (int i = 0; i < 8; i++)
                {
                    switch (i)
                    {
                        case 0:
                            ptr = ref hive.hatredAstros.max;
                            break;
                        case 1:
                            ptr = ref hive.hatredAstros.h1;
                            break;
                        case 2:
                            ptr = ref hive.hatredAstros.h2;
                            break;
                        case 3:
                            ptr = ref hive.hatredAstros.h3;
                            break;
                        case 4:
                            ptr = ref hive.hatredAstros.h4;
                            break;
                        case 5:
                            ptr = ref hive.hatredAstros.h5;
                            break;
                        case 6:
                            ptr = ref hive.hatredAstros.h6;
                            break;
                        case 7:
                            ptr = ref hive.hatredAstros.min;
                            break;
                    }
                    if (!ptr.isNull)
                    {
                        int objectId = ptr.objectId;
                        PlanetData planetData = hive.sector.galaxy.PlanetById(objectId);
                        if (planetData != null && planetData.type != EPlanetType.Gas)
                        {
                            PlanetFactory factory = planetData.factory;
                            if (factory != null)
                            {
                                PowerSystem powerSystem = factory.powerSystem;
                                int consumerCursor = powerSystem.consumerCursor;
                                int nodeCursor = powerSystem.nodeCursor;
                                PowerConsumerComponent[] consumerPool = powerSystem.consumerPool;
                                PowerNodeComponent[] nodePool = powerSystem.nodePool;
                                EntityData[] entityPool = factory.entityPool;
                                TurretComponent[] buffer = factory.defenseSystem.turrets.buffer;
                                double num5 = 0.0;
                                Vector3 vector = Vector3.zero;
                                if (hive._assaultPosByQuadrant == null)
                                {
                                    hive._assaultPosByQuadrant = new Vector3[8];
                                }
                                else
                                {
                                    for (int j = 0; j < 8; j++)
                                    {
                                        hive._assaultPosByQuadrant[j] = Vector3.zero;
                                    }
                                }
                                bool flag3 = false;
                                for (int k = 1; k < consumerCursor; k++)
                                {
                                    ref PowerConsumerComponent ptr2 = ref consumerPool[k];
                                    if (ptr2.id == k)
                                    {
                                        double num6 = 0.01;
                                        int networkId = ptr2.networkId;
                                        PowerNetwork powerNetwork = powerSystem.netPool[networkId];
                                        ref Vector3 ptr3 = ref ptr2.plugPos;
                                        if (powerNetwork != null)
                                        {
                                            long num7 = powerNetwork.energyServed / 4L + powerNetwork.energyCapacity / 80L + (long)((double)ptr2.requiredEnergy * powerNetwork.consumerRatio);
                                            num6 += Math.Sqrt((double)num7 / 500000.0);
                                            int turretId = entityPool[ptr2.entityId].turretId;
                                            if (turretId > 0)
                                            {
                                                ref TurretComponent ptr4 = ref buffer[turretId];
                                                if (ptr4.type == ETurretType.Missile)
                                                {
                                                    num6 *= 10.0;
                                                }
                                                else if (ptr4.type == ETurretType.Plasma)
                                                {
                                                    num6 *= 100.0;
                                                }
                                            }
                                            int num8 = ((ptr3.x >= 0f) ? 1 : 0) + ((ptr3.y >= 0f) ? 2 : 0) + ((ptr3.z >= 0f) ? 4 : 0);
                                            hive._assaultPosByQuadrant[num8] += ptr3 * (float)num6;
                                        }
                                        if (num6 > num5)
                                        {
                                            num5 = num6;
                                            vector = ptr3;
                                            flag3 = true;
                                        }
                                    }
                                }
                                for (int l = 1; l < nodeCursor; l++)
                                {
                                    ref PowerNodeComponent ptr5 = ref nodePool[l];
                                    if (ptr5.id == l)
                                    {
                                        double num9 = 0.01;
                                        int networkId2 = ptr5.networkId;
                                        PowerNetwork powerNetwork2 = powerSystem.netPool[networkId2];
                                        ref Vector3 ptr6 = ref ptr5.powerPoint;
                                        if (powerNetwork2 != null)
                                        {
                                            int powerGenId = entityPool[ptr5.entityId].powerGenId;
                                            long num10 = (powerGenId > 0) ? powerSystem.genPool[powerGenId].generateCurrentTick : 0L;
                                            long num11 = (powerNetwork2.energyServed / 4L + powerNetwork2.energyCapacity / 80L + (long)(ptr5.idleEnergyPerTick / 2) + num10 / 20L) / 2L;
                                            num9 += Math.Sqrt((double)num11 / 500000.0);
                                            int num12 = ((ptr6.x >= 0f) ? 1 : 0) + ((ptr6.y >= 0f) ? 2 : 0) + ((ptr6.z >= 0f) ? 4 : 0);
                                            hive._assaultPosByQuadrant[num12] += ptr5.powerPoint * (float)num9;
                                        }
                                        if (num9 > num5)
                                        {
                                            num5 = num9;
                                            vector = ptr5.powerPoint;
                                            flag3 = true;
                                        }
                                    }
                                }
                                if (flag3)
                                {
                                    flag2 = true;
                                    targetAstroId = ptr.objectId;
                                    float num13 = 0f;
                                    for (int m = 0; m < 8; m++)
                                    {
                                        float magnitude = hive._assaultPosByQuadrant[m].magnitude;
                                        if (magnitude > num13)
                                        {
                                            num13 = magnitude;
                                            tarPos = hive._assaultPosByQuadrant[m];
                                        }
                                    }
                                    maxHatredPos = vector;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (!flag2 && hive.gameData.localPlanet != null && hive.gameData.localPlanet.type != EPlanetType.Gas && hive.gameData.localPlanet.star == hive.starData)
                {
                    flag2 = true;
                    targetAstroId = hive.gameData.localPlanet.astroId;
                    maxHatredPos = (tarPos = hive.sector.skillSystem.playerSkillTargetL);
                }
                if (flag2)
                {
                    int num2 = 5;
                    int num14 = 100 / (num2 * 4 / 5);
                    if (hive.evolve.waves < 3)
                    {
                        num14 = 1;
                    }
                    int num15 = 100 - num14 * (num2);
                    hive.evolve.threat = 0;
                    hive.LaunchLancerAssault(aggressiveLevel, tarPos, maxHatredPos, targetAstroId, count, num14);
                    hive.evolve.threat = 0;
                    hive.evolve.threatshr = 0;
                    hive.evolve.maxThreat = EvolveData.GetSpaceThreatMaxByWaves(hive.evolve.waves, aggressiveLevel);
                    hive.lancerAssaultCountBase += hive.GetLancerAssaultCountIncrement(aggressiveLevel);
                    return;
                }
            }
        }

        // 阻止强行将等级上限设置为30
        [HarmonyPrefix]
        [HarmonyPatch(typeof(EvolveData), "AddExp")]
        public static bool EvolveDataAddExpPrefix(ref EvolveData __instance, int _addexp)
        {
            if (__instance.level >= 100)
            {
                if (__instance.expf != 0 || __instance.expp != 0 || __instance.level != 100)
                {
                    __instance.level = 100;
                    __instance.expf = 0;
                    __instance.expp = 0;
                    __instance.expl = EvolveData.LevelCummulativeExp(100);
                }
                return false;
            }
            __instance.expf += _addexp;
            while (__instance.expf >= EvolveData.levelExps[__instance.level])
            {
                int num = EvolveData.levelExps.Length - 1;
                __instance.expf -= EvolveData.levelExps[__instance.level];
                __instance.expl += EvolveData.levelExps[__instance.level];
                __instance.level++;
                if (__instance.level >= num)
                {
                    __instance.level = num;
                    __instance.expf = 0;
                    return false;
                }
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EvolveData), "AddExpPoint")]
        public static bool EvolveDataAddExpPointPrefix(ref EvolveData __instance, int _addexpp)
        {
            if (__instance.level >= 100)
            {
                if (__instance.expf != 0 || __instance.expp != 0 || __instance.level != 100)
                {
                    __instance.level = 30;
                    __instance.expf = 0;
                    __instance.expp = 0;
                    __instance.expl = EvolveData.LevelCummulativeExp(100);
                }
                return false;
            }
            if (_addexpp > 0)
            {
                __instance.expp += _addexpp;
                if (__instance.expp >= 10000)
                {
                    __instance.expf += __instance.expp / 10000;
                    __instance.expp %= 10000;
                    while (__instance.expf >= EvolveData.levelExps[__instance.level])
                    {
                        int num = EvolveData.levelExps.Length - 1;
                        __instance.expf -= EvolveData.levelExps[__instance.level];
                        __instance.expl += EvolveData.levelExps[__instance.level];
                        __instance.level++;
                        if (__instance.level >= num)
                        {
                            __instance.level = num;
                            __instance.expf = 0;
                            __instance.expp = 0;
                            return false;
                        }
                    }
                }
            }
            return false;
        }

        public static void TryEnableVoidInvasion()
        {
            UIMessageBox.Show("开启虚空入侵".Translate(), "虚空入侵提示".Translate(), 
            "否".Translate(), "是".Translate(), 1, new UIMessageBox.Response(RegretEnable), new UIMessageBox.Response(() =>
            {
                voidInvasionEnabled = true;
            }));
        }

        public static void RegretEnable()
        {

        }

        public static void AskEnableVoidInvasion()
        {
            UIMessageBox.Show("开启虚空入侵".Translate(), "虚空入侵版本更新提示".Translate(),
            "否".Translate(), "是".Translate(), 1, new UIMessageBox.Response(RegretEnable), new UIMessageBox.Response(() =>
            {
                voidInvasionEnabled = true;
            }));
        }

        public static void Import(BinaryReader r)
        {
            InitWhenLoad();
            if(Configs.versionWhenImporting >= 30240716)
            {
                voidInvasionEnabled = r.ReadBoolean();
                difficulty = r.ReadInt32();
                int ahCount = r.ReadInt32();
                for (int i = 0; i < ahCount; i++)
                {
                    AssaultHive ah = new AssaultHive(0, i, i);
                    ah.Import(r);
                    assaultHives.Add(ah);
                }
                modifierEnabled = r.ReadBoolean();
                int mCount = r.ReadInt32();
                for (int i = 0; i < mCount; i++)
                {
                    int mod = r.ReadInt32();
                    if(i < modifier.Count)
                        modifier[i] = mod;
                }
                timeChangedByRelic = r.ReadBoolean();
            }
            UIEscMenuPatch.Init();
            if(Configs.versionWhenImporting < 30240703)
            {
                AskEnableVoidInvasion();
            }
        }

        public static void Export(BinaryWriter w)
        {
            w.Write(voidInvasionEnabled);
            w.Write(difficulty);
            w.Write(assaultHives.Count);
            for (int i = 0; i < assaultHives.Count; i++)
            {
                assaultHives[i].Export(w);
            }
            w.Write(modifierEnabled);
            w.Write(modifier.Count);
            for (int i = 0; i < modifier.Count; i++)
            {
                w.Write(modifier[i]);
            }
            w.Write(timeChangedByRelic);
        }

        public static void IntoOtherSave()
        {
            InitWhenLoad();
            voidInvasionEnabled = false;
        }


        public static EnemyDFHiveSystem TryCreateNewHiveAndCore(int starIndex)
        {
            //EnemyDFHiveSystem hive = TryCreateNewHive(GameMain.galaxy.StarById(starIndex + 1));
            EnemyDFHiveSystem hive = GameMain.spaceSector.TryCreateNewHive(GameMain.galaxy.StarById(starIndex + 1));
            if (hive != null)
            {
                ref GrowthPattern_DFSpace.Builder ptr = ref hive.pbuilders[1];
                if (ptr.instId > 0)
                {
                    return hive;
                }
                int protoId = ptr.protoId;
                EnemyProto enemyProto = LDB.enemies.Select(protoId);
                if (enemyProto != null)
                {
                    EnemyData enemyData = default(EnemyData);
                    enemyData.protoId = (short)protoId;
                    enemyData.modelIndex = (short)enemyProto.ModelIndex;
                    enemyData.astroId = hive.hiveAstroId;
                    enemyData.originAstroId = hive.hiveAstroId;
                    enemyData.owner = 0;
                    enemyData.port = 0;
                    enemyData.dynamic = !enemyProto.IsBuilding;
                    enemyData.isSpace = true;
                    enemyData.localized = true;
                    enemyData.stateFlags = 0;
                    enemyData.pos = ptr.pos;
                    enemyData.rot = ptr.rot;
                    GameMain.spaceSector.AddEnemyDataWithComponents(ref enemyData, hive, 1, false);
                }
                hive.isEmpty = false; // 靠这个才能让他可在星图界面选中、显示轨道，可以显示左上角预警
            }
            return hive;
        }


        public static void BuildLogicGroundBoost()
        {
            //EnemyDFGroundSystem groundSys = GameMain.galaxy.PlanetById(103)?.factory?.enemySystem;
            //if (groundSys == null)
            //{
            //    return;
            //}
            //int cursor = groundSys.builders.cursor;
            //EnemyBuilderComponent[] buffer = groundSys.builders.buffer;
            //ref AnimData[] ptr2 = ref groundSys.factory.enemyAnimPool;
            //ref DFGBaseComponent[] ptr4 = ref groundSys.bases.buffer;
            //ref EnemyData[] ptr5 = ref groundSys.factory.enemyPool;
            //for (int j = 1; j < cursor; j++)
            //{
            //    ref EnemyBuilderComponent ptr8 = ref buffer[j];
            //    if (ptr8.id == j)
            //    {
            //        int enemyId = ptr8.enemyId;
            //        DFGBaseComponent dfgbaseComponent9 = ptr4[(int)ptr5[enemyId].owner];
            //        if (dfgbaseComponent9.evolve.level < testLvlSet)
            //        {
            //            dfgbaseComponent9.evolve.level = testLvlSet;
            //            testLvlSet = -1;
            //        }
            //        GrowthPattern_DFGround.Builder[] pbuilders = dfgbaseComponent9.pbuilders;
            //        ref AnimData anim = ref ptr2[enemyId];
            //        for (int i = 0; i < quickTickFactor; i++)
            //        {
            //            ptr8.energy = ptr8.maxEnergy;
            //            ptr8.matter = ptr8.maxMatter;
            //            ptr8.LogicTick();
            //            if (ptr8.state >= 3)
            //            {
            //                ptr8.BuildLogic_Ground(groundSys, buffer, dfgbaseComponent9);
            //            }
            //        }
            //        ptr8.RefreshAnimation_Ground(pbuilders, ref anim, !groundSys.isLocalLoaded);

            //    }
            //}

        }

        //public static EnemyDFHiveSystem TryCreateNewHive(StarData star)
        //{
        //    SpaceSector sector = GameMain.spaceSector;
        //    if (star == null)
        //    {
        //        return null;
        //    }
        //    int num = 0;
        //    EnemyDFHiveSystem enemyDFHiveSystem = sector.dfHives[star.index];
        //    EnemyDFHiveSystem enemyDFHiveSystem2 = null;
        //    while (enemyDFHiveSystem != null)
        //    {
        //        num |= 1 << enemyDFHiveSystem.hiveOrbitIndex;
        //        enemyDFHiveSystem2 = enemyDFHiveSystem;
        //        enemyDFHiveSystem = enemyDFHiveSystem.nextSibling;
        //    }
        //    int num2 = (enemyDFHiveSystem2 == null) ? 0 : -1;
        //    if (enemyDFHiveSystem2 != null)
        //    {
        //        for (int i = 0; i < 8; i++)
        //        {
        //            if ((num >> i & 1) == 0)
        //            {
        //                num2 = i;
        //                break;
        //            }
        //        }
        //    }
        //    if (num2 == -1)
        //    {
        //        return null;
        //    }
        //    int num3 = star.index * 8 + num2 + 1;
        //    Assert.Null(sector.dfHivesByAstro[num3]);
        //    if (sector.dfHivesByAstro[num3] == null)
        //    {
        //        EnemyDFHiveSystem enemyDFHiveSystem3 = new EnemyDFHiveSystem();
        //        enemyDFHiveSystem3.Init(sector.gameData, star.id, num2);
        //        enemyDFHiveSystem3.SetForNewCreate();
        //        //enemyDFHiveSystem3.SetForNewGame();
        //        if (enemyDFHiveSystem2 != null)
        //        {
        //            enemyDFHiveSystem2.nextSibling = enemyDFHiveSystem3;
        //            enemyDFHiveSystem3.prevSibling = enemyDFHiveSystem2;
        //        }
        //        else
        //        {
        //            sector.dfHives[star.index] = enemyDFHiveSystem3;
        //        }

        //        //float initialGrowth = enemyDFHiveSystem3.history.combatSettings.initialGrowth;
        //        //enemyDFHiveSystem3.ticks = 600 + (int)(num * 72000f) + num2;
        //        //enemyDFHiveSystem3.ticks = (int)((float)enemyDFHiveSystem3.ticks * initialGrowth);
        //        //enemyDFHiveSystem3.ticks = enemyDFHiveSystem3.InitialGrowthToTicks(enemyDFHiveSystem3.ticks);
        //        return enemyDFHiveSystem3;
        //    }
        //    return null;
        //}

        public enum EAssaultModifier
        {
            DamageResist = 0,
            Evade = 1,
            AdditionalArmor = 2,
            ShieldDamageBuff = 3, // 行星护盾增伤
            DropletKiller = 4, // 每次水滴攻击（击杀敌人？）都有概率直接被毁
            NoExp = 5,
            KineticDamageSuppressor = 6, // 直接抑制相关类型的全局伤害参数
            EnergyDamageSuppressor = 7,
            BlastDamageSuppressor = 8,
            MagneticDamageSuppressor = 9,
            SpaceJammer = 10, // 伊卡洛斯的太空舰队无法开火，水滴除外
            DamageBuffSteal = 11, // 直接复制你的部分伤害加成（三类基础类型伤害取最高的，乘系数）
            QuickHeal = 12, // 极速回复生命值
        }
    }
}
