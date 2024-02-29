﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DSP_Battle
{
    public class Relic
    {
        // type 遗物类型 0=legend 1=epic 2=rare 3=common 4=cursed
        // 二进制存储已获取的遗物，需要存档
        public static int[] relics = { 0, 0, 0, 0, 0 };
        // 其他需要存档的数据
        public static int relic0_2Version = 1; // 女神泪重做，老存档此项为0不改效果，新存档此项为1才改效果
        public static int relic0_2Charge = 0; // 新版女神泪充能计数
        public static int relic0_2CanActivate = 1; // 新版女神泪在每次入侵中只能激活一次，激活后设置为0。下次入侵才设置为1
        public static int minShieldPlanetId = -1; // 饮血剑现在会给护盾量最低的星球回盾，但是每秒才更新一次护盾量最低的星球
        public static List<int> recordRelics = new List<int>(); // 被Relic4-6保存的圣物
        public static int autoConstructMegaStructureCountDown = 0;
        public static int autoConstructMegaStructurePPoint = 0;
        public static int trueDamageActive = 0;

        //不存档的设定参数
        public static int relicHoldMax = 8; // 最多可以持有的遗物数
        public static int[] relicNumByType = { 11, 12, 18, 18, 7 }; // 当前版本各种类型的遗物各有多少种，每种类型均不能大于30
        public static double[] relicTypeProbability = { 0.03, 0.06, 0.11, 0.76, 0.04 }; // 各类型遗物刷新的权重
        public static double[] relicTypeProbabilityBuffed = { 0.045, 0.09, 0.165, 0.63, 0.07 }; // 五叶草buff后
        public static int[] modifierByEvent = new int[] { 0, 0, 0, 0, 0, 0 };
        public static double[] relicRemoveProbabilityByRelicCount = { 0, 0, 0, 0, 0.05, 0.1, 0.12, 0.15, 1, 1, 1 }; // 拥有i个reilc时，第三个槽位刷新的是删除relic的概率
        public static double firstRelicIsRare = 0.5; // 第一个遗物至少是稀有的概率
        public static bool canSelectNewRelic = false; // 当canSelectNewRelic为true时点按按钮才是有效的选择
        public static int[] alternateRelics = { -1, -1, -1 }; // 三个备选，百位数字代表稀有度类型，0代表传说，个位十位是遗物序号。
        public const int defaultBasicMatrixCost = 10; // 除每次随机赠送的一次免费随机之外，从第二次开始需要消耗的矩阵的基础值（这个第二次以此基础值的2倍开始）
        public static int basicMatrixCost = 10; // 除每次随机赠送的一次免费随机之外，从第二次开始需要消耗的矩阵的基础值（这个第二次以此基础值的2倍开始）
        public static int rollCount = 0; // 本次连续随机了几次的计数
        public static int AbortReward = 500; // 放弃解译圣物直接获取的矩阵数量
        public static List<int> starsWithMegaStructure = new List<int>(); // 每秒更新，具有巨构的星系。
        public static List<int> starsWithMegaStructureUnfinished = new List<int>(); // 每秒更新，具有巨构且未完成建造的星系.
        public static Vector3 playerLastPos = new VectorLF3(0, 0, 0); // 上一秒玩家的位置
        public static bool alreadyRecalcDysonStarLumin = false; // 不需要存档，如果需要置false则会在读档时以及选择特定遗物时自动完成
        public static int dropletDamageGrowth = 1000; // relic0-10每次水滴击杀的伤害成长
        public static int dropletDamageLimitGrowth = 20000; // relic0-10每次消耗水滴提供的伤害成长上限的成长
        public static int dropletEnergyRestore = 2000000; // relic0-10每次击杀回复的机甲能量
        public static int relic0_2MaxCharge = 1000; // 新版女神泪充能上限
        public static int disturbDamage1612 = 2000; // relic0-8胶囊伤害
        public static int disturbDamage1613 = 3000; // relic0-8胶囊伤害
        public static int energyPerMegaDamage = 10; // tickEnergy开根号后除以此项得到伤害
        public static double ThornmailDamageRatio = 0.2; // relic0-5反伤比例
        public static double ThornmailFieldDamageRatio = 0.2; // relic0-5行星护盾反伤比例

        public static int starIndexWithMaxLuminosity = 0; // 具有最大光度的恒星系的index， 读档时刷新

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameData), "GameTick")]
        public static void RelicGameTick(long time)
        {
            if (time % 60 == 7)
                RefreshStarsWithMegaStructure();
            if (time % 60 == 8)
                RefreshMinShieldPlanet();

        }

        static int N(int num)
        {
            return (int)Math.Pow(2, num);
        }

        public static void InitAllAfterLoad()
        {
            starsWithMegaStructure.Clear();
            starsWithMegaStructureUnfinished.Clear();
            UIRelic.InitAll();
            canSelectNewRelic = false;
            rollCount = 0;
            Configs.relic1_8Protection = int.MaxValue;
            Configs.relic2_17Activated = 0;
            RelicFunctionPatcher.CheckSolarSailLife();
            Configs.eliteDurationFrames = 3600 * 3 + 60 * 20 * Relic.GetCursedRelicCount();
            RefreshConfigs();
            UIEventSystem.InitAll();
            UIEventSystem.InitWhenLoad();
        }

        public static void RefreshConfigs()
        {
            RelicFunctionPatcher.RefreshRerollCost();
            RelicFunctionPatcher.RefreshCargoAccIncTable();
            RelicFunctionPatcher.RefreshDisturbPrefabDesc();
            RelicFunctionPatcher.RefreshBlueBuffStarAssemblyEffect();
        }

        public static int AddRelic(int type, int num)
        {
            if (num > 30) return -1; // 序号不存在
            if (type > 4 || type < 0) return -2; // 稀有度不存在
            if ((relics[type] & 1 << num) > 0) return 0; // 已有
            if (GetRelicCount() >= relicHoldMax) return -3; // 超上限

            // 下面是一些特殊的Relic在选择时不是简单地改一个拥有状态就行，需要单独对待if (type == 0 && num == 2)
            if (type == 0 && num == 2)
            {
                relics[type] |= 1 << num;
                relic0_2Version = 1;
                relic0_2Charge = 0;
                relic0_2CanActivate = 1;
            }
            else if ((type == 0 && num == 3) || (type == 4 && num == 0))
            {
                relics[type] |= 1 << num;
                RelicFunctionPatcher.CheckAndModifyStarLuminosity(type*100+num);
            }
            else if ((type == 1 && num == 4))
            {
                relics[type] |= 1 << num;
                //GameMain.data.history.UnlockTechUnlimited(1918, false);
                GameMain.data.history.UnlockTechUnlimited(1919, false);
                GameMain.data.mainPlayer.TryAddItemToPackage(9511, 3, 0, true);
                Utils.UIItemUp(9511, 3);
            }
            else if (type == 1 && num == 10)
            {
                trueDamageActive = 1;
            }
            else if (type == 3 && num == 5)
            {
                RelicFunctionPatcher.ReInitBattleRoundAndDiff();
            }
            else if (type == 3 && num == 8)
            {
                if (GameMain.history.techStates.ContainsKey(1002) && GameMain.history.techStates[1002].unlocked)
                {
                    GameMain.mainPlayer.ThrowTrash(6001, 1000, 0, 0);
                }
                if (GameMain.history.techStates.ContainsKey(1111) && GameMain.history.techStates[1111].unlocked)
                {
                    GameMain.mainPlayer.ThrowTrash(6002, 800, 0, 0);
                }
                if (GameMain.history.techStates.ContainsKey(1124) && GameMain.history.techStates[1124].unlocked)
                {
                    GameMain.mainPlayer.ThrowTrash(6003, 600, 0, 0);
                }
                if (GameMain.history.techStates.ContainsKey(1312) && GameMain.history.techStates[1312].unlocked)
                {
                    GameMain.mainPlayer.ThrowTrash(6004, 400, 0, 0);
                }
                if (GameMain.history.techStates.ContainsKey(1705) && GameMain.history.techStates[1705].unlocked)
                {
                    GameMain.mainPlayer.ThrowTrash(6001, 400, 0, 0);
                }
            }
            else if (type == 4 && num == 6)
            {
                if (Relic.HaveRelic(4, 6)) return 0;
                recordRelics.Clear();
                int count = 0;
                const int maxCount = 3; // 最多记录三个
                for (int haveType = 4; haveType < 5 && count < maxCount; haveType = (haveType + 1) % 5)
                {
                    for (int haveNum = 0; haveNum < relicNumByType[haveType] && count < maxCount; haveNum++)
                    {
                        if (Relic.HaveRelic(haveType, haveNum))
                        {
                            recordRelics.Add(haveType * 100 + haveNum);
                            count++;
                        }
                    }
                    if (haveType == 3)
                        break;
                }
                relics[type] |= 1 << num;
            }
            else
            {
                relics[type] |= 1 << num;
            }
            RefreshConfigs();
            return 1;
        }

        public static void AskRemoveRelic(int removeType, int removeNum)
        {
            if (removeType > 4 || removeNum > 30)
            {
                UIMessageBox.Show("Failed".Translate(), "Failed. Unknown relic.".Translate(), "确定".Translate(), 1);
                RegretRemoveRelic();
                return;
            }
            else if (!Relic.HaveRelic(removeType, removeNum))
            {
                UIMessageBox.Show("Failed".Translate(), "Failed. Relic not have.".Translate(), "确定".Translate(), 1);
                RegretRemoveRelic();
                return;
            }
            UIMessageBox.Show("删除遗物确认标题".Translate(), String.Format( "删除遗物确认警告".Translate(), ("遗物名称" + removeType.ToString() + "-" + removeNum.ToString()).Translate().Split('\n')[0]),
            "否".Translate(), "是".Translate(), 1, new UIMessageBox.Response(RegretRemoveRelic), new UIMessageBox.Response(() =>
            {
                RemoveRelic(removeType, removeNum);

                //UIMessageBox.Show("成功移除！".Translate(), "已移除遗物描述".Translate() + ("遗物名称" + removeType.ToString() + "-" + removeNum.ToString()).Translate().Split('\n')[0], "确定".Translate(), 1);

                UIRelic.CloseSelectionWindow();
                UIRelic.HideSlots();
            }));
        }

        public static void RemoveRelic(int removeType, int removeNum)
        {
            if (Relic.HaveRelic(removeType, removeNum))
            {
                relics[removeType] = relics[removeType] ^ 1 << removeNum;
                UIRelic.RefreshSlotsWindowUI();
                RefreshConfigs();
            }
        }

        public static void RegretRemoveRelic()
        {
            canSelectNewRelic = true;
        }

        public static bool HaveRelic(int type, int num)
        {
            //if (Configs.developerMode &&( type == 0 && num == 5 ||  type == 1 && num == 9 )) return true;
            if (type > 4 || type < 0 || num > 30) return false;
            if ((relics[type] & (1 << num)) > 0) return true;
            return false;
        }

        public static bool isRecorded(int type, int num)
        {
            return recordRelics.Contains(type * 100 + num);
        }

        // 输出遗物数量，type输入-1为获取全部类型的遗物数量总和
        public static int GetRelicCount(int type = -1)
        {
            if (type < 0 || type > 4)
            {
                return GetRelicCount(0) + GetRelicCount(1) + GetRelicCount(2) + GetRelicCount(3) + GetRelicCount(4);
            }
            else
            {
                int r = relics[type];
                int count = 0;
                while (r > 0)
                {
                    r = r & (r - 1);
                    count++;
                }
                int recorded = 0;
                if (recordRelics.Count > 0)
                {
                    foreach (var item in recordRelics)
                    {
                        if(item / 100 == type)
                            recorded++;
                    }
                }
                return count - recorded;
            }
        }

        // 返回受诅咒的遗物的数量
        public static int GetCursedRelicCount()
        {
            return GetRelicCount(4);
        }

        // 允许玩家选择一个新的遗物
        public static bool PrepareNewRelic(int bonusRollCount = 0)
        {
            //if (GetRelicCount() >= relicHoldMax) return false;
            rollCount = -1 - bonusRollCount; // 从-1开始是因为每次准备给玩家新的relic都要重新随机一次
            canSelectNewRelic = true;

            
            UIRelic.OpenSelectionWindow();
            UIRelic.ShowSlots(); // 打开已有遗物栏

            return true;
        }


        public static void InitRelicData()
        {

        }


        // 刷新保存当前存在巨构的星系
        public static void RefreshStarsWithMegaStructure()
        {
            starsWithMegaStructure.Clear();
            starsWithMegaStructureUnfinished.Clear();
            for (int i = 0; i < GameMain.data.galaxy.starCount; i++)
            {
                if (GameMain.data.dysonSpheres.Length > i)
                {
                    DysonSphere sphere = GameMain.data.dysonSpheres[i];
                    if (sphere != null)
                    {
                        starsWithMegaStructure.Add(i);
                        if (sphere.totalStructurePoint + sphere.totalCellPoint - sphere.totalConstructedStructurePoint - sphere.totalConstructedCellPoint > 0)
                        {
                            starsWithMegaStructureUnfinished.Add(i);
                        }
                    }
                }
            }
        }

        // 刷新保存护盾量最低的行星
        public static void RefreshMinShieldPlanet()
        {
            
        }

        public static bool Verify(double possibility)
        {
            if ((relics[4] & 1 << 1) > 0) // relic4-1负面效果：概率减半
                possibility = 0.5 * possibility; 
            if (Utils.RandDouble() < possibility)
                return true;
            else if ((relics[0] & 1 << 9) > 0) // 具有增加幸运的遗物，则可以再判断一次
                return (Utils.RandDouble() < possibility);

            return false;
        }

        // 任何额外伤害都需要经过此函数来计算并处理，dealDamage默认为false，代表只用这个函数计算而尚未实际造成伤害
        public static int BonusDamage(double damage, double bonus)
        {
            if (HaveRelic(2, 13))
            {
                bonus = 2 * bonus * damage;
            }
            else
            {
                bonus = bonus * damage;
            }
            return (int)bonus;
        }

        public static int BonusedDamage(double damage, double bonus)
        {
            if (HaveRelic(2, 13))
            {
                bonus = 2 * bonus * damage;
            }
            else
            {
                bonus = bonus * damage;
            }
            return (int)(damage + bonus);
        }

        // 有限制地建造某一(starIndex为-1时则是随机的)巨构的固定数量(amount)的进度，不因层数、节点数多少而改变一次函数建造的进度量
        public static void AutoBuildMegaStructure(int starIndex = -1, int amount = 12, int frameCost = 5)
        {
            if (starsWithMegaStructureUnfinished.Count <= 0)
                return;
            if (starIndex < 0)
            {
                starIndex = starsWithMegaStructureUnfinished[Utils.RandInt(0, starsWithMegaStructureUnfinished.Count)]; // 可能会出现点数被浪费的情况，因为有的巨构就差一点cell完成，差的那些正在吸附，那么就不会立刻建造，这些amount就被浪费了，但完全建成的巨构不会被包含在这个列表中，前面的情况也不会经常发生，所以不会经常大量浪费
            }
            if (starIndex >= 0 && starIndex < GameMain.data.dysonSpheres.Length)
            {
                DysonSphere sphere = GameMain.data.dysonSpheres[starIndex];
                if (sphere != null)
                {
                    for (int i = 0; i < sphere.layersIdBased.Length; i++)
                    {
                        DysonSphereLayer dysonSphereLayer = sphere.layersIdBased[i];
                        if (dysonSphereLayer != null)
                        {
                            int num = dysonSphereLayer.nodePool.Length;
                            for (int j = 0; j < num; j++)
                            {
                                DysonNode dysonNode = dysonSphereLayer.nodePool[j];
                                if (dysonNode != null)
                                {
                                    for (int k = 0; k < Math.Min(6, amount/frameCost); k++)
                                    {
                                        if (dysonNode.spReqOrder > 0)
                                        {
                                            sphere.OrderConstructSp(dysonNode);
                                            sphere.ConstructSp(dysonNode);
                                            amount -= frameCost; // 框架结构点数由于本身是需要火箭才能建造的，自然比细胞点数昂贵一些。这里默认设置为昂贵五倍。
                                        }
                                    }
                                    for (int l = 0; l < Math.Min(6, amount); l++)
                                    {
                                        if (dysonNode.cpReqOrder > 0)
                                        {
                                            dysonNode.cpOrdered++;
                                            dysonNode.ConstructCp();
                                            amount--;
                                        }
                                    }
                                }
                                if (amount <= 0) return;
                            }
                        }
                    }
                }
            }
        }


        public static void Export(BinaryWriter w)
        {
            w.Write(relics[0]);
            w.Write(relics[1]);
            w.Write(relics[2]);
            w.Write(relics[3]);
            w.Write(relics[4]);
            w.Write(relic0_2Version);
            w.Write(relic0_2Charge);
            w.Write(relic0_2CanActivate);
            w.Write(minShieldPlanetId);
            w.Write(recordRelics.Count);
            foreach (var item in recordRelics)
            {
                w.Write(item);
            }
            w.Write(autoConstructMegaStructureCountDown);
            w.Write(autoConstructMegaStructurePPoint);
            w.Write(trueDamageActive);
        }

        public static void Import(BinaryReader r)
        {
            if (Configs.versionWhenImporting >= 30221025)
            {
                relics[0] = r.ReadInt32();
                relics[1] = r.ReadInt32();
                relics[2] = r.ReadInt32();
                relics[3] = r.ReadInt32();
                if (Configs.versionWhenImporting >= 30230519)
                    relics[4] = r.ReadInt32();
                else
                    relics[4] = 0;
                RelicFunctionPatcher.CheckAndModifyStarLuminosity();
            }
            else
            {
                relics[0] = 0;
                relics[1] = 0;
                relics[2] = 0;
                relics[3] = 0;
                relics[4] = 0;
            }
            if (Configs.versionWhenImporting >= 30230426)
            {
                relic0_2Version = r.ReadInt32();
                relic0_2Charge = r.ReadInt32();
                relic0_2CanActivate = r.ReadInt32();
                minShieldPlanetId = r.ReadInt32();
            }
            else
            {
                relic0_2Version = 0;
                relic0_2Charge = 0;
                relic0_2CanActivate = 1;
                minShieldPlanetId = -1;
            }
            recordRelics.Clear();
            if (Configs.versionWhenImporting >= 30230523)
            {
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    recordRelics.Add(r.ReadInt32());
                }
            }
            autoConstructMegaStructureCountDown = r.ReadInt32();
            autoConstructMegaStructurePPoint = r.ReadInt32();
            trueDamageActive = r.ReadInt32();
            InitAllAfterLoad();
        }

        public static void IntoOtherSave()
        {
            relics[0] = 0;
            relics[1] = 0;
            relics[2] = 0;
            relics[3] = 0;
            relics[4] = 0;
            recordRelics.Clear();
            autoConstructMegaStructureCountDown = 0;
            autoConstructMegaStructurePPoint = 0;
            trueDamageActive = 0;
            InitAllAfterLoad();
        }
    }


    public class RelicFunctionPatcher
    {
        public static float r0 = 50;
        public static float r1 = 1;
        public static float r2 = 1;
        public static float r3 = 1;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameData), "GameTick")]
        public static void RelicFunctionGameTick(long time)
        {
            if (time % 60 == 8)
                CheckMegaStructureAttack();
            //else if (time % 60 == 9)
            //    AutoChargeShieldByMegaStructure();
            else if (time % 60 == 10)
                CheckPlayerHasaki();

            TryRecalcDysonLumin();
            AutoBuildMega();
            AutoBuildMegaOfMaxLuminStar(time);
        }



        /// <summary>
        /// relic 0-1 1-6 2-4 2-11 2-8 3-0 3-6 3-14
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="power"></param>
        /// <param name="productRegister"></param>
        /// <param name="consumeRegister"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AssemblerComponent), "InternalUpdate")]
        public static bool AssemblerInternalUpdatePatch(ref AssemblerComponent __instance, float power, int[] productRegister, int[] consumeRegister)
        {
            if (power < 0.1f)
                return true;

            if (__instance.recipeType == ERecipeType.Assemble)
            {
                // relic1-6
                if (Relic.HaveRelic(1, 6))
                {
                    if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                    {
                        int rocketId = __instance.products[0];
                        int rodNum = -1;
                        if (rocketId >= 9488 && rocketId <= 9490)
                            rodNum = 2;
                        else if (rocketId == 9491 || rocketId == 9492 || rocketId == 9510 || rocketId == 1503)
                            rodNum = 1;

                        if (rodNum > 0 && __instance.served[rodNum] < 10 * __instance.requireCounts[rodNum]) // 判断原材料是否已满
                        {
                            //if (__instance.served[rodNum] > 0)
                            //    __instance.incServed[rodNum] += __instance.incServed[rodNum] / __instance.served[rodNum] * 2; // 增产点数也要返还
                            __instance.incServed[rodNum] += 8; // 返还满级增产点数
                            __instance.served[rodNum] += 2;
                            int[] obj = consumeRegister;
                            lock (obj)
                            {
                                consumeRegister[__instance.requires[rodNum]] -= 2;
                            }
                        }
                    }
                }

                // relic2-4
                if (__instance.products[0] == 1801 || __instance.products[0] == 1802)
                {
                    if (Relic.HaveRelic(2, 4) && __instance.requires.Length > 1)
                    {
                        if (__instance.served[1] < 10 * __instance.requireCounts[1])
                        {
                            if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                            {
                                __instance.incServed[1] += 20; // 返还满级增产点数
                                __instance.served[1] += 5;
                                int[] obj = consumeRegister;
                                lock (obj)
                                {
                                    consumeRegister[__instance.requires[1]] -= 5;
                                }
                            }
                            if (__instance.extraTime >= __instance.extraTimeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                            {
                                __instance.incServed[1] += 20; // 返还满级增产点数
                                __instance.served[1] += 5;
                                int[] obj = consumeRegister;
                                lock (obj)
                                {
                                    consumeRegister[__instance.requires[1]] -= 5;
                                }
                            }

                        }
                    }
                }
                else if (__instance.products[0] == 1501 && Relic.HaveRelic(3, 0)) // relic3-0
                {
                    if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                    {
                        __instance.produced[0]++;
                        int[] obj = productRegister;
                        lock (obj)
                        {
                            productRegister[1501] += 1;
                        }
                    }
                    if (__instance.extraTime >= __instance.extraTimeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                    {
                        __instance.produced[0]++;
                        int[] obj = productRegister;
                        lock (obj)
                        {
                            productRegister[1501] += 1;
                        }
                    }
                }
                else if ((__instance.products[0] == 1303 || __instance.products[0] == 1305) && Relic.HaveRelic(3, 6)) // relic3-6
                {
                    if (__instance.replicating)
                    {
                        __instance.extraTime += (int)(0.5 * __instance.extraSpeed);
                    }
                }
                else if ((__instance.products[0] == 1203 || __instance.products[0] == 1204) && Relic.HaveRelic(3, 14)) // relic3-14
                {
                    int reloadNum = __instance.products[0] == 1203 ? 2 : 1;
                    if (__instance.served[reloadNum] < 10 * __instance.requireCounts[reloadNum])
                    {
                        if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                        {
                            __instance.incServed[reloadNum] += 4;
                            __instance.served[reloadNum] += 1;
                            int[] obj = consumeRegister;
                            lock (obj)
                            {
                                consumeRegister[__instance.requires[reloadNum]] -= 1;
                            }
                        }
                        if (__instance.extraTime >= __instance.extraTimeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                        {
                            __instance.incServed[reloadNum] += 4;
                            __instance.served[reloadNum] += 1;
                            int[] obj = consumeRegister;
                            lock (obj)
                            {
                                consumeRegister[__instance.requires[reloadNum]] -= 1;
                            }
                        }

                    }
                }

                // relic0-1 蓝buff效果 要放在最后面，因为前面有加time的遗物，所以这个根据time结算的要放在最后
                if (Relic.HaveRelic(0, 1) && __instance.requires.Length > 1)
                {
                    // 原材料未堆积过多才会返还，产物堆积未被取出则不返还。黑棒产线无视此遗物效果
                    if (__instance.served[0] < 10 * __instance.requireCounts[0] && __instance.products[0] != 1803)
                    {
                        // Utils.Log("time = " + __instance.time + " / " + __instance.timeSpend); 这里是能输出两个相等的值的
                        // 不能直接用__instance.time >= __instance.timeSpend代替，必须-1，即便已经相等却无法触发，为什么？
                        if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                        {
                            //if(__instance.served[0] > 0)
                            //    __instance.incServed[0] += __instance.incServed[0] / __instance.served[0] * __instance.productCounts[0]; // 增产点数也要返还
                            __instance.incServed[0] += 4 * __instance.productCounts[0]; // 返还满级增产点数
                            __instance.served[0] += __instance.productCounts[0]; // 注意效果是每产出一个产物返还一个1号材料而非每次产出，因此还需要在extraTime里再判断回填原料
                            int[] obj = consumeRegister;
                            lock (obj)
                            {
                                consumeRegister[__instance.requires[0]] -= __instance.productCounts[0];
                            }
                        }
                        if (__instance.extraTime >= __instance.extraTimeSpend - 1 && __instance.produced[0] < 10 * __instance.productCounts[0])
                        {
                            //if (__instance.served[0] > 0)
                            //    __instance.incServed[0] += __instance.incServed[0] / __instance.served[0] * __instance.productCounts[0];
                            __instance.incServed[0] += 4 * __instance.productCounts[0]; // 返还满级增产点数
                            __instance.served[0] += __instance.productCounts[0];
                            int[] obj = consumeRegister;
                            lock (obj)
                            {
                                consumeRegister[__instance.requires[0]] -= __instance.productCounts[0];
                            }
                        }

                    }
                }

            }
            else if (__instance.recipeType == ERecipeType.Chemical)
            {
                // relic0-2 老女神之泪效果
                //if (Relic.HaveRelic(0, 2) && __instance.requires.Length > 1)
                //{
                //    if (__instance.served[0] < 20 * __instance.requireCounts[0])
                //    {
                //        if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] < 20 * __instance.productCounts[0])
                //        {
                //            //if (__instance.served[0] > 0)
                //            //    __instance.incServed[0] += __instance.incServed[0] / __instance.served[0] * __instance.requireCounts[0];
                //            __instance.incServed[0] += 4 * __instance.requireCounts[0];
                //            __instance.served[0] += __instance.requireCounts[0];
                //            int[] obj = consumeRegister;
                //            lock (obj)
                //            {
                //                consumeRegister[__instance.requires[0]] -= __instance.requireCounts[0];
                //            }
                //        }
                //    }
                //}
            }
            else if (__instance.recipeType == ERecipeType.Smelt)
            {
                // relic 2-11 副产物提炼
                if (Relic.HaveRelic(2, 11))
                {
                    if (__instance.time >= __instance.timeSpend - 1 && __instance.produced[0] + __instance.productCounts[0] < 100 && Relic.Verify(0.3))
                    {
                        __instance.produced[0]++;
                        int[] obj = productRegister;
                        lock (obj)
                        {
                            productRegister[__instance.products[0]] += 1;
                        }
                    }
                    if (__instance.extraTime >= __instance.extraTimeSpend - 1 && __instance.produced[0] + __instance.productCounts[0] < 100 && Relic.Verify(0.3))
                    {
                        __instance.produced[0]++;
                        int[] obj = productRegister;
                        lock (obj)
                        {
                            productRegister[__instance.products[0]] += 1;
                        }
                    }

                }
            }
            else if (__instance.recipeType == ERecipeType.Particle && Relic.HaveRelic(2, 8)) // relic2-8
            {
                if (__instance.products.Length > 1 && __instance.products[0] == 1122)
                {
                    if (__instance.replicating)
                    {
                        __instance.extraTime += (int)(power * __instance.speedOverride * 5); // 因为extraSpeed填满需要正常speed填满的十倍
                    }
                    __instance.produced[1] = -5;
                }

            }
            return true;
        }

        public static void RefreshBlueBuffStarAssemblyEffect()
        {
            if (Relic.HaveRelic(0, 1))
                MoreMegaStructure.StarAssembly.blueBuffByTCFV = 1;
            else
                MoreMegaStructure.StarAssembly.blueBuffByTCFV = 0;
        }


        /// <summary>
        /// relic0-2
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="entityId"></param>
        /// <param name="offset"></param>
        /// <param name="filter"></param>
        /// <param name="needs"></param>
        /// <param name="stack"></param>
        /// <param name="inc"></param>
        /// <param name="__result"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlanetFactory), "PickFrom")]
        public static void AutoProliferate(ref PlanetFactory __instance, int entityId, int offset, int filter, int[] needs, ref byte stack, ref byte inc, ref int __result)
        {
            if (!Relic.HaveRelic(0, 2)) return;
            int itemId = __result;
            if (itemId == 0) return;

            var _this = __instance;
            int beltId = _this.entityPool[entityId].beltId;
            if (beltId <= 0)
            {
                int assemblerId = _this.entityPool[entityId].assemblerId;
                if (assemblerId > 0)
                {
                    Mutex obj = _this.entityMutexs[entityId];
                    lock (obj)
                    {
                        int[] products = _this.factorySystem.assemblerPool[assemblerId].products;
                        int num = products.Length;
                        for (int i = 0; i < num; i++)
                        {
                            if (products[i] == itemId)
                            {
                                inc = (byte)(4 * stack);
                                return;
                            }
                        }
                        return;
                    }
                }
                int labId = _this.entityPool[entityId].labId;
                if(labId > 0)
                {
                    Mutex obj = _this.entityMutexs[entityId];
                    lock (obj)
                    {
                        int[] products = _this.factorySystem.labPool[labId].products;
                        int num = products.Length;
                        for (int i = 0; i < num; i++)
                        {
                            if (products[i] == itemId)
                            {
                                inc = (byte)(4 * stack);
                                return;
                            }
                        }
                        return;
                    }
                    return;
                }
            }
        }


        /// <summary>
        /// relic0-4
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), "GameTick_Gamma")]
        public static void GammaReceiverPatch(ref PowerGeneratorComponent __instance)
        {
            if (Relic.HaveRelic(0, 4) && __instance.catalystPoint < 3600)
            {
                __instance.catalystPoint = 3500; // 为什么不是3600，因为3600在锅盖消耗后会计算一个透镜消耗
                __instance.catalystIncPoint = 14000; // 4倍是满增产
            }
        }

        /// <summary>
        /// relic0-4
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="eta"></param>
        /// <param name="__result"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), "EnergyCap_Gamma_Req")]
        public static bool EnergyCapGammaReqPatch(ref PowerGeneratorComponent __instance, float eta, ref long __result)
        {
            if (!Relic.HaveRelic(0, 4))
                return true;

            __instance.currentStrength = 1;
            float num2 = (float)Cargo.accTableMilli[__instance.catalystIncLevel];
            __instance.capacityCurrentTick = (long)(__instance.currentStrength * (1f + __instance.warmup * 1.5f) * ((__instance.catalystPoint > 0) ? (2f * (1f + num2)) : 1f) * ((__instance.productId > 0) ? 8f : 1f) * (float)__instance.genEnergyPerTick);
            eta = 1f - (1f - eta) * (1f - __instance.warmup * __instance.warmup * 0.4f);
            __instance.warmupSpeed = 0.25f * 4f * 1.3888889E-05f;
            __result = (long)((double)__instance.capacityCurrentTick / (double)eta + 0.49999999);
            return false;
        }

        /// <summary>
        /// relic 0-5 2-16 3-12 虚空荆棘反弹伤害，各种护盾伤害减免和规避
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="caster"></param>
        /// <param name="damage"></param>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SkillSystem), "MechaEnergyShieldResist", new Type[] { typeof(SkillTarget), typeof(int) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref})]
        public static bool ThornmailAttackPreMarker(int damage, ref int __state)
        {
            __state = damage;
            return true;
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SkillSystem), "MechaEnergyShieldResist", new Type[] { typeof(SkillTargetLocal), typeof(int), typeof(int) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
        public static bool ThornmailLocalAttackPreMarker(int damage, ref int __state)
        {
            __state = damage;
            return true;
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SkillSystem), "MechaEnergyShieldResist", new Type[] { typeof(SkillTarget), typeof(int) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref })]
        public static void ThornmailAttackPostHandler(SkillTarget caster, int damage, ref int __state)
        {
            if (Relic.HaveRelic(0, 5))
            {
                SkillTarget casterPlayer;
                casterPlayer.id = 1;
                casterPlayer.type = ETargetType.Player;
                casterPlayer.astroId = 0;
                int realDamage = (int)((__state - damage) * GameMain.data.history.energyDamageScale * Relic.ThornmailDamageRatio);
                GameMain.data.spaceSector.skillSystem.DamageObject(realDamage, 1, ref caster, ref casterPlayer);
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SkillSystem), "MechaEnergyShieldResist", new Type[] { typeof(SkillTargetLocal), typeof(int), typeof(int) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
        public static void ThornmailAttackPostHandler(SkillTargetLocal caster, int astroId, int damage, ref int __state)
        {
            if (Relic.HaveRelic(0, 5))
            {
                SkillTarget target;
                target.id = caster.id;
                target.astroId = astroId;
                target.type = caster.type;
                SkillTarget casterPlayer;
                casterPlayer.id = 1;
                casterPlayer.type = ETargetType.Player;
                casterPlayer.astroId = 0;
                int realDamage = (int)((__state - damage) * GameMain.data.history.energyDamageScale * Relic.ThornmailDamageRatio);
                GameMain.data.spaceSector.skillSystem.DamageObject(realDamage, 1, ref target, ref casterPlayer);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SkillSystem), "DeterminePlanetATFieldRaytestInStar")]
        public static void ThornmailFieldAttckHandler(ref SkillSystem __instance, int starAstroId, ERayTestSkillType skillType, int skillId, int __result)
        {
            if(Relic.HaveRelic(0,5) && __result > 0 && skillId > 0)
            {
                ref var _this = ref __instance;
                if (skillType == ERayTestSkillType.humpbackPlasma)
                {
                    int cursor = _this.humpbackProjectiles.cursor;
                    GeneralExpImpProjectile[] buffer = _this.humpbackProjectiles.buffer;
                    if (skillId < cursor)
                    {
                        ref GeneralExpImpProjectile ptr = ref buffer[skillId];
                        if (ptr.id == skillId)
                        {
                            int realDamage = (int)(ptr.damage * Relic.ThornmailFieldDamageRatio * GameMain.data.history.energyDamageScale);
                            __instance.DamageObject(realDamage, 1, ref ptr.caster, ref ptr.target);
                        }
                    }
                }
                else if (skillType == ERayTestSkillType.lancerSpacePlasma)
                {
                    int cursor = _this.lancerSpacePlasma.cursor;
                    GeneralProjectile[] buffer = _this.lancerSpacePlasma.buffer;
                    if (skillId < cursor)
                    {
                        ref GeneralProjectile ptr = ref buffer[skillId];
                        if (ptr.id == skillId)
                        {
                            int realDamage = (int)(ptr.damage * Relic.ThornmailFieldDamageRatio * GameMain.data.history.energyDamageScale);
                            __instance.DamageObject(realDamage, 1, ref ptr.caster, ref ptr.target);
                        }
                    }
                }
                else if (skillType == ERayTestSkillType.lancerLaserOneShot)
                {
                    int cursor = _this.lancerLaserOneShots.cursor;
                    SpaceLaserOneShot[] buffer = _this.lancerLaserOneShots.buffer;
                    if (skillId < cursor)
                    {
                        ref SpaceLaserOneShot ptr = ref buffer[skillId];
                        if (ptr.id == skillId)
                        {
                            int realDamage = (int)(ptr.damage * Relic.ThornmailFieldDamageRatio * GameMain.data.history.energyDamageScale);
                            __instance.DamageObject(realDamage, 1, ref ptr.caster, ref ptr.target);
                        }
                    }
                }
                else if (skillType == ERayTestSkillType.lancerLaserSweep)
                {
                    int cursor = _this.lancerLaserSweeps.cursor;
                    SpaceLaserSweep[] buffer = _this.lancerLaserSweeps.buffer;
                    if (skillId < cursor)
                    {
                        ref SpaceLaserSweep ptr = ref buffer[skillId];
                        if (ptr.id == skillId && (ptr.lifemax - ptr.life) % ptr.damageInterval == 0)
                        {
                            int realDamage = (int)(ptr.damage * Relic.ThornmailFieldDamageRatio * GameMain.data.history.energyDamageScale);
                            SkillTarget emptyCaster;
                            emptyCaster.id = 0;
                            emptyCaster.type = ETargetType.None;
                            emptyCaster.astroId = starAstroId;
                            __instance.DamageObject(realDamage, 1, ref ptr.caster, ref emptyCaster);
                        }
                    }
                }
                else if (skillType == ERayTestSkillType.spaceLaserSweep)
                {
                    int cursor = _this.spaceLaserSweeps.cursor;
                    SpaceLaserSweep[] buffer = _this.spaceLaserSweeps.buffer;
                    if (skillId < cursor)
                    {
                        ref SpaceLaserSweep ptr = ref buffer[skillId];
                        if (ptr.id == skillId)
                        {
                            int realDamage = (int)(ptr.damage * Relic.ThornmailFieldDamageRatio * GameMain.data.history.energyDamageScale); 
                            SkillTarget emptyCaster;
                            emptyCaster.id = 0;
                            emptyCaster.type = ETargetType.None;
                            emptyCaster.astroId = starAstroId;
                            __instance.DamageObject(realDamage, 1, ref ptr.caster, ref emptyCaster);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// relic 0-6
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="consumeRegister"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TurretComponent), "LoadAmmo")]
        public static bool LudensSealPatch(ref TurretComponent __instance, ref int[] consumeRegister)
        {
            if (!Relic.HaveRelic(0, 6))
                return true;
            else
            {
                ref var _this = ref __instance;
                if (_this.itemCount == 0 || _this.bulletCount > 0)
                {
                    return false;
                }
                int num = (int)((float)_this.itemInc / (float)_this.itemCount + 0.5f);
                num = ((num > 10) ? 10 : num);
                short num2 = (short)((double)_this.itemBulletCount * Cargo.incTableMilli[num] + ((_this.itemBulletCount < 12) ? 0.51 : 0.1));
                _this.bulletCount = (short)(_this.itemBulletCount + num2);
                //_this.itemCount -= 1;
                //_this.itemInc -= (short)num;
                _this.currentBulletInc = (byte)num;
                consumeRegister[(int)_this.itemId]++;
                return false;
            }
        }


        /// <summary>
        /// relic0-7
        /// </summary>
        public static void CheckMegaStructureAttack()
        {
            if (!Relic.HaveRelic(0, 7))
                return;

            SpaceSector sector = GameMain.data.spaceSector;
            if (sector == null) return;
            EnemyData[] pool = GameMain.data.spaceSector.enemyPool;
            for (int i = 0; i < sector.enemyCursor; i++)
            {
                ref EnemyData e = ref pool[i];
                if (e.unitId <= 0 || e.id <= 0)
                    continue;

                EnemyDFHiveSystem[] hivesByAstro = sector.dfHivesByAstro;
                EnemyDFHiveSystem hive = hivesByAstro[e.originAstroId - 1000000];
                int starIndex = hive?.starData?.index ?? -1;
                if (starIndex >= 0 && GameMain.data.dysonSpheres != null)
                {
                    DysonSphere sphere = GameMain.data.dysonSpheres[starIndex];
                    if (sphere != null && sphere.energyGenCurrentTick > 0)
                    {
                        long tickEnergy = sphere.energyGenCurrentTick;
                        int damage = (int)(Math.Pow(tickEnergy, 0.5) / Relic.energyPerMegaDamage);
                        if (starIndex < 1000 && MoreMegaStructure.MoreMegaStructure.StarMegaStructureType[starIndex] == 6)
                            damage *= 2;
                        damage = Relic.BonusDamage(damage, 1);
                        SkillTarget target;
                        SkillTarget caster;
                        target.id = e.id;
                        target.astroId = e.originAstroId;
                        target.type = ETargetType.Enemy;
                        caster.id = 1;
                        caster.type = ETargetType.Player;
                        caster.astroId = 0;
                        sector.skillSystem.DamageObject(damage, 1, ref target, ref caster);
                    }
                }
            }
        }


        /// <summary>
        /// relic 0-8
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="skillSystem"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(LocalDisturbingWave), "TickSkillLogic")]
        public static bool DisturbingDamagePatch(ref LocalDisturbingWave __instance, ref SkillSystem skillSystem)
        {
            ref var _this = ref __instance;
            if (_this.life <= 0)
            {
                return false;
            }
            _this.currentDiffuseRadius += _this.diffusionSpeed * 0.016666668f;
            if (_this.caster.id == 0)
            {
                return false;
            }
            float num = _this.thickness * 0.5f;
            float num2 = _this.currentDiffuseRadius - num;
            float num3 = _this.currentDiffuseRadius + num;
            if (num2 < 0f)
            {
                num2 = 0f;
            }
            if (num3 > _this.diffusionMaxRadius)
            {
                num3 = _this.diffusionMaxRadius;
                _this.life = 0;
            }
            float num4 = num2 * num2;
            float num5 = num3 * num3;
            float num6 = 0.016666668f;
            PlanetFactory planetFactory = skillSystem.astroFactories[_this.astroId];
            EnemyData[] enemyPool = planetFactory.enemyPool;
            EnemyUnitComponent[] buffer = planetFactory.enemySystem.units.buffer;
            int[] consumeRegister = null;
            int num7 = 0;
            TurretComponent[] array = null;
            int num8 = 0;
            int num9 = 0;
            bool flag = (_this.caster.type == ETargetType.None || _this.caster.type == ETargetType.Ruin) && planetFactory.entityPool[_this.caster.id].turretId > 0; // 判断条件额外增加了ruin是自己设定的
            if (flag)
            {
                num7 = planetFactory.entityPool[_this.caster.id].turretId;
                array = planetFactory.defenseSystem.turrets.buffer;
                VSLayerMask vslayerMask = array[num7].vsCaps & array[num7].vsSettings;
                num8 = (int)(vslayerMask & VSLayerMask.GroundHigh);
                num9 = (int)((int)(vslayerMask & VSLayerMask.AirHigh) >> 2);
                consumeRegister = GameMain.statistics.production.factoryStatPool[planetFactory.index].consumeRegister;
            }
            Vector3 normalized = _this.center.normalized;
            float num10 = _this.diffusionMaxRadius;
            if (flag)
            {
                ref TurretComponent ptr = ref array[num7];
                HashSystem hashSystemDynamic = planetFactory.hashSystemDynamic;
                int[] hashPool = hashSystemDynamic.hashPool;
                int[] bucketOffsets = hashSystemDynamic.bucketOffsets;
                int[] bucketCursors = hashSystemDynamic.bucketCursors;
                TurretSearchPair[] turretSearchPairs = planetFactory.defenseSystem.turretSearchPairs;
                int num11 = ptr.searchPairBeginIndex + ptr.searchPairCount;
                for (int i = ptr.searchPairBeginIndex; i < num11; i++)
                {
                    if (turretSearchPairs[i].searchType == ESearchType.HashBlock)
                    {
                        int searchId = turretSearchPairs[i].searchId;
                        int num12 = bucketOffsets[searchId];
                        int num13 = bucketCursors[searchId];
                        for (int j = 0; j < num13; j++)
                        {
                            int num14 = num12 + j;
                            int num15 = hashPool[num14];
                            if (num15 != 0)
                            {
                                int num16 = num15 >> 28;
                                if ((1 << num16 & (int)_this.mask) != 0)
                                {
                                    int num17 = num15 & 268435455;
                                    if (num16 == 4)
                                    {
                                        ref EnemyData ptr2 = ref enemyPool[num17];
                                        if (ptr2.id == num17 && !ptr2.isInvincible && ptr2.unitId != 0)
                                        {
                                            Vector3 vector = (Vector3)ptr2.pos - _this.center;
                                            Vector3 vector2 = Vector3.Dot(normalized, vector) * normalized - vector;
                                            float num18 = vector2.x * vector2.x + vector2.y * vector2.y + vector2.z * vector2.z;
                                            if (num18 >= num4 && num18 <= num5)
                                            {
                                                ref EnemyUnitComponent ptr3 = ref buffer[ptr2.unitId];
                                                float num19 = (2f - Mathf.Sqrt(num18) / _this.diffusionMaxRadius) * 0.5f * _this.disturbStrength;
                                                if (ptr3.disturbValue < num19)
                                                {
                                                    bool flag2 = true;
                                                    if (ptr.IsAirEnemy((int)ptr2.protoId))
                                                    {
                                                        if (num9 == 0)
                                                        {
                                                            flag2 = false;
                                                        }
                                                    }
                                                    else if (num8 == 0)
                                                    {
                                                        flag2 = false;
                                                    }
                                                    if (ptr3.disturbValue + num6 < num19)
                                                    {
                                                        if (flag2 && ptr.bulletCount == 0)
                                                        {
                                                            if (ptr.itemCount > 0)
                                                            {
                                                                ptr.LoadAmmo(consumeRegister);
                                                            }
                                                            else
                                                            {
                                                                flag2 = false;
                                                            }
                                                        }
                                                        if (flag2 && _this.caster.type == ETargetType.None) // 由relic1-11发射的额外波，设置为casterType是ruin，不消耗额外弹药
                                                        {
                                                            ref TurretComponent ptr4 = ref ptr;
                                                            ptr4.bulletCount -= 1;
                                                        }
                                                    }
                                                    if (flag2)
                                                    {
                                                        // 造成伤害
                                                        if (Relic.HaveRelic(0, 8))
                                                        {
                                                            int realDamage = ptr.itemId == 1612 ? Relic.disturbDamage1612 : Relic.disturbDamage1613;
                                                            realDamage = (int)(realDamage * 1.0 * GameMain.history.magneticDamageScale);
                                                            realDamage = Relic.BonusDamage(realDamage, 1);
                                                            SkillTargetLocal skillTargetLocal = default(SkillTargetLocal);
                                                            skillTargetLocal.type = ETargetType.Enemy;
                                                            skillTargetLocal.id = ptr2.id;
                                                            skillSystem.DamageGroundObjectByLocalCaster(planetFactory, realDamage, 1, ref skillTargetLocal, ref _this.caster);
                                                        }
                                                        // 原逻辑
                                                        ptr3.disturbValue = num19;
                                                        DFGBaseComponent dfgbaseComponent = planetFactory.enemySystem.bases[(int)ptr2.owner];
                                                        if (dfgbaseComponent != null && dfgbaseComponent.id == (int)ptr2.owner)
                                                        {
                                                            skillSystem.AddGroundEnemyHatred(dfgbaseComponent, ref ptr2, ETargetType.None, _this.caster.id, (int)(num19 * 800f + 0.5f));
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// 所有免费建造随机巨构的效果结算
        /// </summary>
        public static void AutoBuildMega()
        {
            if(Relic.autoConstructMegaStructurePPoint >= 1000)
            {
                Relic.autoConstructMegaStructureCountDown += Relic.autoConstructMegaStructurePPoint / 1000;
                Relic.autoConstructMegaStructurePPoint = Relic.autoConstructMegaStructurePPoint % 1000;
            }
            if (Relic.autoConstructMegaStructureCountDown > 0)
            {
                Relic.AutoBuildMegaStructure(-1, 120);
                Relic.autoConstructMegaStructureCountDown--;
            }
        }


        /// <summary>
        /// relic 1-0
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameHistoryData), "NotifyTechUnlock")]
        public static void AutoConstructMegaWhenTechUnlock()
        {
            if (Relic.HaveRelic(1, 0))
                Relic.autoConstructMegaStructureCountDown += 10 * 60;
        }

        /// <summary>
        /// relic 1-1
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TurretComponent), "InternalUpdate")]
        public static void TurrentComponentPostPatch(ref TurretComponent __instance)
        {
            ref var _this = ref __instance;
            // relic 1-1
            if(Relic.HaveRelic(1,1))
            {
                if (_this.supernovaStrength == 30f)
                {
                    _this.supernovaTick = 1501;
                }
                if (_this.supernovaTick >= 900)
                {
                    _this.supernovaStrength = 29.46f;
                }
            }

        }

        /// <summary>
        /// relic1-2
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlanetATField), "BreakShield")]
        public static bool BreakFieldPostPatch(ref PlanetATField __instance)
        {
            if (__instance.recoverCD <= 0)
            {
                __instance.energy = __instance.energyMax;
                __instance.recoverCD = 36000;
            }
            else
            {
                __instance.energy = 0L;
                if (__instance.rigidTime == 0)
                {
                    __instance.recoverCD = Math.Max(360, __instance.recoverCD);
                }
                __instance.ClearFieldResistHistory();
            }
            return false;
        }


        /// <summary>
        /// relic 1-3 1-10 2-12
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="damage"></param>
        /// <param name="slice"></param>
        /// <param name="target"></param>
        /// <param name="caster"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SkillSystem), "DamageObject")]
        public static bool DamageObjectPrePatch(ref SkillSystem __instance, ref int damage, int slice, ref SkillTarget target, ref SkillTarget caster)
        {
            bool r0103 = Relic.HaveRelic(1, 3);
            bool r0109 = Relic.HaveRelic(1, 9);
            bool r0110 = Relic.trueDamageActive > 0;
            bool r0212 = Relic.HaveRelic(2, 12);
            if (r0103 || r0109 || r0110 || r0212)
            {
                ref var _this = ref __instance;
                float factor = 1.0f;
                int antiArmor = 0;
                int astroId = target.astroId;
                if (astroId > 1000000)
                {
                    if (target.type == ETargetType.Enemy)
                    {
                        EnemyDFHiveSystem enemyDFHiveSystem = _this.sector.dfHivesByAstro[astroId - 1000000];
                        int starIndex = enemyDFHiveSystem?.starData?.index ?? -1;
                        if (r0103 && starIndex >= 0 && starIndex < GameMain.data.dysonSpheres.Length)
                        {
                            DysonSphere sphere = GameMain.data.dysonSpheres[starIndex];
                            if (sphere != null)
                                factor += (float)(3 * (1.0 - sphere.energyDFHivesDebuffCoef));
                        }
                        if (r0110 && enemyDFHiveSystem != null)
                        {
                            int level = enemyDFHiveSystem.evolve.level;
                            int num2 = 100 / slice;
                            int num3 = level * num2 / 2;
                            antiArmor = num3;
                        }
                    }
                    else if (target.type == ETargetType.Craft && r0109)
                    {
                        ///////////////////////////////////////////////////////////////////////////////////////
                    }
                }
                else if (astroId > 100 && astroId <= 204899 && astroId % 100 > 0)
                {
                    if (caster.astroId == astroId)
                    {
                        return true; // 交由DamageGroundObjectByLocalCaster的prePatch自行处理，因为这个DamageGroundObjectByLocalCaster不止被DamageObject调用，还被各种skill的TickSkillLogic调用
                    }
                    else
                    {
                        return true; // 也交由DamageGroundObjectByRemoteCaster的prePatch自行处理
                    }
                }
                else if (astroId % 100 == 0 && target.type == ETargetType.Craft)
                {

                }
                damage = Relic.BonusedDamage(damage, factor) + antiArmor;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SkillSystem), "DamageGroundObjectByLocalCaster")]
        public static bool DamageGroundObjectByLocalCasterPrePatch(ref SkillSystem __instance, PlanetFactory factory, ref int damage, int slice, ref SkillTargetLocal target)
        {
            if (target.id <= 0)
            {
                return true;
            }
            bool r0109 = Relic.HaveRelic(1, 9);
            bool r0110 = Relic.trueDamageActive > 0;
            bool r0212 = Relic.HaveRelic(2, 12);
            if (r0109 || r0110 || r0212)
            {
                ref var _this = ref __instance;
                float factor = 1.0f;
                int antiArmor = 0;
                if (target.type == ETargetType.Enemy)
                {
                    ref EnemyData ptr2 = ref factory.enemyPool[target.id];
                    if (ptr2.id != target.id || ptr2.isInvincible)
                    {
                        return true;
                    }
                    DFGBaseComponent dfgbaseComponent = null;
                    if (ptr2.owner > 0)
                    {
                        dfgbaseComponent = factory.enemySystem.bases[(int)ptr2.owner];
                        if (dfgbaseComponent.id != (int)ptr2.owner)
                        {
                            dfgbaseComponent = null;
                        }
                    }
                    if (dfgbaseComponent != null)
                    {
                        int level = dfgbaseComponent.evolve.level;
                        int num2 = 100 / slice;
                        int num3 = level * num2 / 5;
                        antiArmor = num3;
                    }
                }
                else if (target.type == ETargetType.Craft)
                {

                }

                damage = Relic.BonusedDamage(damage, factor) + antiArmor;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SkillSystem), "DamageGroundObjectByRemoteCaster")]
        public static bool DamageGroundObjectByRemoteCastPrePatch(ref SkillSystem __instance, PlanetFactory factory, ref int damage, int slice, ref SkillTargetLocal target)
        {
            if (target.id <= 0)
            {
                return true;
            }
            bool r0109 = Relic.HaveRelic(1, 9);
            bool r0110 = Relic.trueDamageActive > 0;
            bool r0212 = Relic.HaveRelic(2, 12);
            if (r0109 || r0110 || r0212)
            {
                ref var _this = ref __instance;
                float factor = 1.0f;
                int antiArmor = 0;
                if (target.type == ETargetType.Enemy)
                {
                    ref EnemyData ptr2 = ref factory.enemyPool[target.id];
                    if (ptr2.id != target.id || ptr2.isInvincible)
                    {
                        return true;
                    }
                    DFGBaseComponent dfgbaseComponent = null;
                    if (ptr2.owner > 0)
                    {
                        dfgbaseComponent = factory.enemySystem.bases[(int)ptr2.owner];
                        if (dfgbaseComponent.id != (int)ptr2.owner)
                        {
                            dfgbaseComponent = null;
                        }
                    }
                    if (dfgbaseComponent != null)
                    {
                        int level = dfgbaseComponent.evolve.level;
                        int num2 = 100 / slice;
                        int num3 = level * num2 / 5;
                        antiArmor = num3;
                    }
                }
                else if (target.type == ETargetType.Craft)
                {

                }

                damage = Relic.BonusedDamage(damage, factor) + antiArmor;
            }
            return true;
        }

        /// <summary>
        /// relic1-7 relic3-11
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="gameTick"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(DysonSphereLayer), "GameTick")]
        public static void DysonLayerGameTickPostPatchToAccAbsorb(ref DysonSphereLayer __instance, long gameTick)
        {
            DysonSwarm swarm = __instance.dysonSphere.swarm;
            if (Relic.HaveRelic(1, 7))
            {
                int num = (int)(gameTick % 40L);
                for (int i = 1; i < __instance.nodeCursor; i++)
                {
                    DysonNode dysonNode = __instance.nodePool[i];
                    if (dysonNode != null && dysonNode.id == i && dysonNode.id % 40 == num && dysonNode.sp == dysonNode.spMax)
                    {
                        dysonNode.OrderConstructCp(gameTick, swarm);
                    }
                }
            }
            if (Relic.HaveRelic(3, 1))
            {
                int num = (int)(gameTick % 120L);
                for (int i = 1; i < __instance.nodeCursor; i++)
                {
                    DysonNode dysonNode = __instance.nodePool[i];
                    if (dysonNode != null && dysonNode.id == i && dysonNode.id % 120 == num && dysonNode.sp == dysonNode.spMax)
                    {
                        dysonNode.OrderConstructCp(gameTick, swarm);
                    }
                }
            }
        }

        /// <summary>
        /// relic 1-11
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="factory"></param>
        /// <param name="pdesc"></param>
        /// <param name="power"></param>
        /// <param name="gameTick"></param>
        /// <param name="combatUpgradeData"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TurretComponent), "Shoot_Disturb")]
        public static bool ShootDisturbPostPatch(ref TurretComponent __instance, PlanetFactory factory, PrefabDesc pdesc, float power, long gameTick, ref CombatUpgradeData combatUpgradeData)
        {
            if (power < 0.1f || (__instance.bulletCount == 0 && __instance.itemCount == 0))
            {
                return false;
            }
            int num = __instance.phasePos;
            int num2 = pdesc.turretRoundInterval / pdesc.turretROF;
            int flag = 0;
            if (num % num2 == (int)(gameTick % (long)num2))
            {
                flag = 1;
            }
            else if (Relic.HaveRelic(1, 11))
            {
                if (num % num2 == (int)((gameTick - 30) % (long)num2))
                    flag = 2;
                else if (num % num2 == (int)((gameTick - 60) % (long)num2))
                    flag = 2;
            }
            if (flag > 0)
            {
                ref LocalDisturbingWave ptr = ref GameMain.data.spaceSector.skillSystem.turretDisturbingWave.Add();
                ptr.astroId = factory.planetId;
                ptr.protoId = (int)__instance.itemId;
                ptr.center = factory.entityPool[__instance.entityId].pos;
                ptr.rot = factory.entityPool[__instance.entityId].rot;
                ptr.mask = ETargetTypeMask.Enemy;
                ptr.caster.type = flag == 1 ? ETargetType.None : ETargetType.Ruin;
                ptr.caster.id = __instance.entityId;
                ptr.disturbStrength = (float)__instance.bulletDamage * pdesc.turretDamageScale * combatUpgradeData.magneticDamageScale * power * 0.01f * flag;
                ptr.thickness = 2.5f;
                ptr.diffusionSpeed = 45f;
                ptr.diffusionMaxRadius = pdesc.turretMaxAttackRange;
                ptr.StartToDiffuse();
            }      
            return false;
        }

        /// <summary>
        /// trlic 1-11
        /// </summary>
        public static void RefreshDisturbPrefabDesc()
        {
            if(Relic.HaveRelic(1, 11))
            {
                PlanetFactory.PrefabDescByModelIndex[422].turretMaxAttackRange = 80;
                PlanetFactory.PrefabDescByModelIndex[422].turretDamageScale = 2;
            }
            else
            {
                PlanetFactory.PrefabDescByModelIndex[422].turretMaxAttackRange = 40;
                PlanetFactory.PrefabDescByModelIndex[422].turretDamageScale = 1;
            }
        }


        /// <summary>
        /// relic2-5 3-10
        /// </summary>
        public static void CheckPlayerHasaki()
        {
            if (Relic.HaveRelic(2, 5) || Relic.HaveRelic(3, 10))
            {
                Vector3 pos = GameMain.mainPlayer.position;
                if (pos.x != Relic.playerLastPos.x || pos.y != Relic.playerLastPos.y || pos.z != Relic.playerLastPos.z)
                {
                    if (Relic.HaveRelic(2, 5) && Relic.Verify(0.08))
                    {
                        GameMain.mainPlayer.TryAddItemToPackage(9500, 1, 0, true);
                        Utils.UIItemUp(9500, 1, 200);
                    }
                    if (Relic.HaveRelic(3, 10) && Relic.Verify(0.03))
                    {
                        GameMain.mainPlayer.TryAddItemToPackage(9500, 1, 0, true);
                        Utils.UIItemUp(9500, 1, 200);
                    }

                    Relic.playerLastPos = new Vector3(pos.x, pos.y, pos.z);
                }
            }
        }


        /// <summary>
        /// relic3-0 解锁科技时调用重新计算太阳帆寿命并覆盖
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameHistoryData), "UnlockTechFunction")]
        public static void UnlockTechPostPatch()
        {
            CheckSolarSailLife();
        }

        /// <summary>
        /// relic3-0 重新计算太阳帆寿命
        /// </summary>
        public static void CheckSolarSailLife()
        {
            if (!Relic.HaveRelic(3, 0)) return;
            float solarSailLife = 540;
            if (GameMain.history.techStates.ContainsKey(3106) && GameMain.history.techStates[3106].unlocked)
            {
                solarSailLife += 360;
            }
            else if (GameMain.history.techStates.ContainsKey(3105) && GameMain.history.techStates[3105].unlocked)
            {
                solarSailLife += 270;
            }
            else if (GameMain.history.techStates.ContainsKey(3104) && GameMain.history.techStates[3104].unlocked)
            {
                solarSailLife += 180;
            }
            else if (GameMain.history.techStates.ContainsKey(3103) && GameMain.history.techStates[3103].unlocked)
            {
                solarSailLife += 120;
            }
            else if (GameMain.history.techStates.ContainsKey(3102) && GameMain.history.techStates[3102].unlocked)
            {
                solarSailLife += 60;
            }
            else if (GameMain.history.techStates.ContainsKey(3101) && GameMain.history.techStates[3101].unlocked)
            {
                solarSailLife += 30;
            }
            GameMain.history.solarSailLife = solarSailLife;
        }

        /// <summary>
        /// relic3-2
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Mecha), "GenerateEnergy")]
        public static void MechaEnergyBonusRestore(ref Mecha __instance)
        {
            if (Relic.HaveRelic(3, 2))
            {
                double change = __instance.reactorPowerGen * 0.5 / 60;
                __instance.coreEnergy += change;
                GameMain.mainPlayer.mecha.MarkEnergyChange(0, change); // 算在燃烧室发电
                if (__instance.coreEnergy > __instance.coreEnergyCap) __instance.coreEnergy = __instance.coreEnergyCap;
            }
        }

        /// <summary>
        /// relic3-4
        /// </summary>
        public static void ReInitBattleRoundAndDiff()
        {
            
        }

        /// <summary>
        /// relic3-15
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="time"></param>
        /// <param name="dt"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Mecha), "GameTick")]
        public static void MechaGameTickPostPatch(Mecha __instance, long time, float dt)
        {
            if (Relic.HaveRelic(3, 15))
            {
                __instance.lab.GameTick(time, dt);
                __instance.lab.GameTick(time, dt);
                __instance.lab.GameTick(time, dt);
                __instance.lab.GameTick(time, dt);
            }
        }

        /// <summary>
        /// relic0-3。如果移除了relic0-3则需要重新进游戏才能应用，因为不太好算就特别地写一个移除relic0-3的计算了。
        /// </summary>
        public static void CheckAndModifyStarLuminosity(int newRelic = -1) // -1代表是游戏加载存档的操作，按顺序进行0-3和4-0的计算即可。如果不是-1，则序号代表此次增加的relic是哪个，则按条件计算
        {
            if (Relic.HaveRelic(0, 3) && (newRelic == -1 || newRelic == 3))
            {
                if (Relic.HaveRelic(4, 0) && newRelic == 3) // 说明是此次仅增加relic0-3，之前已经有了relic4-0编织者额负面buff了，则先还原其负面buff
                {
                    float maxL = 0;
                    for (int i = 0; i < GameMain.galaxy.starCount; i++)
                    {
                        StarData starData = GameMain.galaxy.stars[i];
                        if ((starData != null) && (starData.luminosity > maxL))
                        {
                            maxL = starData.luminosity;
                            Relic.starIndexWithMaxLuminosity = i;
                        }
                    }
                    for (int i = 0; i < GameMain.galaxy.starCount; i++)
                    {
                        StarData starData = GameMain.galaxy.stars[i];
                        if (starData != null && i != Relic.starIndexWithMaxLuminosity)
                            starData.luminosity /= (float)Math.Pow(0.7, 0.33000001311302185); // 此处是除
                    }
                }

                for (int i = 0; i < GameMain.galaxy.starCount; i++)
                {
                    StarData starData = GameMain.galaxy.stars[i];
                    if (starData != null)
                        starData.luminosity = (float)(Math.Pow((Mathf.Round((float)Math.Pow((double)starData.luminosity, 0.33000001311302185) * 1000f) / 1000f + 1.0), 1.0 / 0.33000001311302185) - starData.luminosity);

                }
                //还要重新计算并赋值每个戴森球之前已初始化好的属性
                Relic.alreadyRecalcDysonStarLumin = false;
            }
            if (Relic.HaveRelic(4, 0)) // 无论如何只要有relic4-0都要计算一遍，因为，即使只是addrelic0-3，那么在其功能里已经还原过4-0了，因此还要再正向计算一次4-0把debuff加回来
            {
                float maxL = 0;
                for (int i = 0; i < GameMain.galaxy.starCount; i++)
                {
                    StarData starData = GameMain.galaxy.stars[i];
                    if((starData != null) && (starData.luminosity > maxL))
                    {
                        maxL = starData.luminosity;
                        Relic.starIndexWithMaxLuminosity = i;
                    }
                }
                for (int i = 0;i< GameMain.galaxy.starCount;i++)
                {
                    StarData starData = GameMain.galaxy.stars[i];
                    if (starData != null && i != Relic.starIndexWithMaxLuminosity)
                        starData.luminosity *= (float)Math.Pow(0.7, 0.33000001311302185);
                }
                //还要重新计算并赋值每个戴森球之前已初始化好的属性
                Relic.alreadyRecalcDysonStarLumin = false;
            }
        }




        /// <summary>
        /// 每帧调用检查，不能在import的时候调用，会因为所需的DysonSphere是null而无法完成重新计算和赋值
        /// </summary>
        public static void TryRecalcDysonLumin()
        {
            if (!Relic.alreadyRecalcDysonStarLumin && (Relic.HaveRelic(0, 3) || Relic.HaveRelic(4, 0)))
            {
                for (int i = 0; i < GameMain.galaxy.starCount; i++)
                {
                    if (i < GameMain.data.dysonSpheres.Length && GameMain.data.dysonSpheres[i] != null)
                    {
                        DysonSphere sphere = GameMain.data.dysonSpheres[i];
                        double num5 = (double)sphere.starData.dysonLumino;
                        sphere.energyGenPerSail = (long)(400.0 * num5);
                        sphere.energyGenPerNode = (long)(1500.0 * num5);
                        sphere.energyGenPerFrame = (long)(1500 * num5);
                        sphere.energyGenPerShell = (long)(300 * num5);
                    }
                }
                Relic.alreadyRecalcDysonStarLumin = true;
            }
        }

        /// <summary>
        /// Relic 4-0 自动建造光度最高星系的巨构
        /// </summary>
        /// <param name="time"></param>
        public static void AutoBuildMegaOfMaxLuminStar(long time)
        {
            int timeStep = 2;
            if (GameMain.data.dysonSpheres.Length > Relic.starIndexWithMaxLuminosity && GameMain.data.dysonSpheres[Relic.starIndexWithMaxLuminosity] != null)
            {
                DysonSphere sphere = GameMain.data.dysonSpheres[Relic.starIndexWithMaxLuminosity];
                long energy = sphere.energyGenCurrentTick_Layers;
                if (energy > 16666666667) // 1T
                    timeStep = 4;
                else if (energy > 1000000000) // 60G
                    timeStep = 60;
                else if (energy > 16666667) // 1G
                    timeStep = 10;
                else
                    timeStep = 2;
            }
            if (Relic.HaveRelic(4, 0) && time % timeStep == 1)
            {
                Relic.AutoBuildMegaStructure(Relic.starIndexWithMaxLuminosity, 70, 30);
            }
        }

        /// <summary>
        /// relic 4-3
        /// </summary>
        public static void RefreshCargoAccIncTable()
        {
            if (Relic.HaveRelic(4,3))
            {
                Cargo.accTable = new int[] { 0, 200, 350, 500, 750, 1000, 1250, 1500, 1750, 2000, 2250 };
                Cargo.accTableMilli = new double[] { 0.0, 0.200, 0.350, 0.500, 0.750, 1.000, 1.250, 1.500, 1.750, 2.000, 2.250 };
                Cargo.incTable = new int[] { 0, 225, 250, 275, 300, 325, 350, 375, 400, 425, 450 };
                Cargo.incTableMilli = new double[] { 0.0, 0.225, 0.250, 0.275, 0.300, 0.325, 0.350, 0.375, 0.400, 0.425, 0.45 };
            }
            else
            {
                Cargo.accTable = new int[] { 0, 250, 500, 750, 1000, 1250, 1500, 1750, 2000, 2250, 2500 };
                Cargo.accTableMilli = new double[] { 0.0, 0.250, 0.500, 0.750, 1.000, 1.250, 1.500, 1.750, 2.000, 2.250, 2.500 };
                Cargo.incTable = new int[] { 0, 125, 200, 225, 250, 275, 300, 325, 350, 375, 400 };
                Cargo.incTableMilli = new double[] { 0.0, 0.125, 0.200, 0.225, 0.250, 0.275, 0.300, 0.325, 0.350, 0.375, 0.400 };
            }
        }

        /// <summary>
        /// Relic 4-4 启迪回响自动建造恒星要塞，当巨构框架或节点被建造时
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(DysonSphere), "ConstructSp")]
        public static void AutoBuildStarFortressWhenNodeConstructed(ref DysonSphere __instance)
        {
            if (Relic.HaveRelic(4, 4))
            {
                int starIndex = __instance.starData.index;
                List<int> needCompModules = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    if (StarFortress.moduleComponentCount[starIndex][i] + StarFortress.moduleComponentInProgress[starIndex][i] < StarFortress.moduleMaxCount[starIndex][i] * StarFortress.compoPerModule[i])
                        needCompModules.Add(i);
                }
                if (needCompModules.Count > 0)
                {
                    int moduleIndex = needCompModules[Utils.RandInt(0, needCompModules.Count)];
                    StarFortress.moduleComponentCount[starIndex][moduleIndex] += 1;
                }
            }
        }

        public static void RefreshRerollCost()
        {
            //if (Relic.HaveRelic(4, 1))
            //    Relic.basicMatrixCost = (int)(0.5 * Relic.defaultBasicMatrixCost);
            //else
            //    Relic.basicMatrixCost = Relic.defaultBasicMatrixCost;
        }
    }
}
