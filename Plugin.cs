using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PotionCraft.LocalizationSystem;
using PotionCraft.FactionSystem;
using PotionCraft.Npc.Parts;
using PotionCraft.ManagersSystem;
using PotionCraft.ManagersSystem.Npc;
using PotionCraft.ManagersSystem.Room;
using PotionCraft.NotificationSystem;
using System;
using System.Collections.Generic;
using QFSW.QC;
using UnityEngine;

namespace ByAppointmentOnly
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        // Place to log messages for debug purposes
        private static ManualLogSource logSource;

        // Dictionary of npcTemplate names and strings used for notifications
        private static Dictionary<string, string> targetNames;

        // Buffer to store template for requested merchant
        private static NpcTemplate queueJumper;

        // Indicates whether we should intercept the npc queue and block merchant npcs
        private static bool interceptQueue;

        private void Awake()
        {
            logSource = Logger;
            logSource.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            targetNames = new Dictionary<string, string>();
            
            targetNames.Add("Alchemist", "An alchemist");
            targetNames.Add("WMerchant", "A peddler");
            targetNames.Add("Dwarf", "A miner");;
            // targetNames.Add("Herbalist", "A herbalist");
            // targetNames.Add("Mushroomer", "A forager");
            // targetNames.Add("EvilDude", "The Poison Guy");

            queueJumper = null;

            interceptQueue = false;

            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        // Class:       PotionCraft.LocalizationSystem.LocalizationManager
        // Function:    private static void ParseLocalizationData()
        [HarmonyPostfix, HarmonyPatch(typeof(LocalizationManager), "ParseLocalizationData")]
        public static void ParseLocalizationData_Postfix()
        {
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_merchant_request", "Merchant Request Sent!");
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_refund_request", "Merchant Request Refunded!");
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_request_sent", "{0} will arrive tomorrow");
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_request_changed_1", "Okay, {0} will arrive tomorrow");
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_request_changed_2", "Fine, {0} will arrive tomorrow");
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_request_changed_3", "Very well, {0} will arrive tomorrow");
            LocalizationManager.textData[LocalizationManager.Locale.en].AddText("BAO_request_cancelled", "Doesn't cost anything right now though");
        }

        // Class:       PotionCraft.ManagersSystem.Npc.NpcManager
        // Function:    public List<Tuple<Faction, FactionClass, NpcTemplate>> GetGeneratedQueue()
        //[HarmonyPostfix, HarmonyPatch(typeof(NpcManager), "GetGeneratedQueue")]
        public static void GetGeneratedQueue_PostFix(ref List<Tuple<Faction, FactionClass, NpcTemplate>> __result)
        {
            if (interceptQueue)
            {
                foreach (Tuple<Faction, FactionClass, NpcTemplate> npcInQueue in __result)
                {
                    logSource.LogInfo(string.Concat(new string[]
                    {
                        "faction: ",
                        (npcInQueue.Item1 == null) ? "null" : (npcInQueue.Item1.name ?? ""),
                        ", class: ",
                        (npcInQueue.Item2 == null) ? "null" : npcInQueue.Item2.name,
                        ", template: ",
                        (npcInQueue.Item3 == null) ? "null" : npcInQueue.Item3.name
                    }));
                }
            }
        }

        // Class:       PotionCraft.ManagersSystem.Npc.NpcManager
        // Function:    private void SpawnNpcOnDayStart()
        [HarmonyPrefix, HarmonyPatch(typeof(NpcManager), "SpawnNpcOnDayStart")]
        public static void SpawnNpcOnDayStart_Prefix()
        {
            if (!PassDayRequirement(10))
            {
                logSource.LogInfo("You are not ready to mess with the queue. Wait a few days");
                return;
            }

            if (!PassChapterRequirement())
            {
                logSource.LogInfo("You are not ready to mess with the queue. Complete more of the alchemists path");
                return;
            }

            if (!PassPopularityRequirement())
            {
                logSource.LogInfo("You are not ready to mess with the queue. Become more popular");
                return;
            }

            interceptQueue = true;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(NpcManager), "SpawnNpcOnDayStart")]
        public static void SpawnNpcOnDayStart_Postfix()
        {
            if (interceptQueue)
            {
                interceptQueue = false;
                if (queueJumper != null)
                {
                    Managers.Npc.AddToQueueForSpawn(queueJumper, null, null, true);
                    queueJumper = null;
                }
            }
        }

        // Class:       PotionCraft.ManagersSystem.Npc.NpcManager
        // Function:    public void AddToQueueForSpawn(NpcTemplate npcTemplate, Faction npcFaction = null, FactionClass npcFactionClass = null, bool toTheStart = false)
        [HarmonyPrefix, HarmonyPatch(typeof(NpcManager), "AddToQueueForSpawn")]
        public static bool AddToQueueForSpawn_Prefix(ref NpcTemplate npcTemplate, ref Faction npcFaction, ref FactionClass npcFactionClass, ref bool toTheStart)
        {
            if (interceptQueue)
            {
                if (targetNames.ContainsKey(npcTemplate.name))
                {
                    logSource.LogInfo(string.Format("Blocking {0} from queue", npcTemplate.name));
                    return false;
                }
            }
            logSource.LogInfo(string.Format("NPC {0} added to queue", npcTemplate.name));
            return true;
        }

        [Command("BAORequestAlchemist", "Requests that an alchemist visit the shop tomorrow", true, true, Platform.AllPlatforms, MonoTargetType.Single)]
        private static void RequestAlchemist()
        {
            if (CmdRoomCheck())
            {
                SendMerchantRequest("Alchemist");
            }
        }
        
        [Command("BAORequestMiner", "Requests that a miner visit the shop tomorrow", true, true, Platform.AllPlatforms, MonoTargetType.Single)]
        public static void RequestMiner()
        {
            if (CmdRoomCheck())
            {
                SendMerchantRequest("Dwarf");
            }
        }
        
        [Command("BAORequestPeddler", "Requests that a peddler visit the shop tomorrow", true, true, Platform.AllPlatforms, MonoTargetType.Single)]
        public static void RequestPeddler()
        {
            if (CmdRoomCheck())
            {
                SendMerchantRequest("WMerchant");
            }
        }

        [Command("BAORequestRefund", "Cancels a request for a merchant", true, true, Platform.AllPlatforms, MonoTargetType.Single)]
        public static void RequestRefund()
        {
            if (CmdRoomCheck())
            {
                CancelRequest();
            }
        }

        public static bool CmdRoomCheck()
        {
            // check current room. commands only work in the bedroom
            RoomManager.RoomIndex room = Managers.Room.currentRoom;
            if (room != RoomManager.RoomIndex.Bedroom)
            {
                logSource.LogInfo("Commands can only be used from the bedroom atm");
                return false;
            }
            return true;
        }

        public static bool SendMerchantRequest(string merchant)
        {
            NpcTemplate npcMerchant = NpcTemplate.GetByName(merchant);
            if (npcMerchant == null)
            {
                logSource.LogError(string.Format("Merchant '{0}' not found", merchant));
                return false;
            }
            else
            {
                string msgKey = "BAO_request_sent";
                if (queueJumper != null)
                {
                    int randNum = new System.Random().Next(1, 3);
                    msgKey = string.Format("BAO_request_cancelled_{0}", randNum);
                }

                string title = new Key("BAO_merchant_request").GetText();
                string desc = string.Format(new Key(msgKey).GetText(), merchant);
                Notification.ShowText(title, desc, Notification.TextType.EventText);
                logSource.LogInfo(string.Format("Merchant '{0}' will arrive tomorrow", merchant));
                queueJumper = npcMerchant;
            }
            return true;
        }

        public static bool CancelRequest()
        {
            if (queueJumper == null)
            {
                logSource.LogInfo("Refund requested, but there was no request");
                return false;
            }

            string title = new Key("BAO_refund_request").GetText();
            string desc = new Key("BAO_refund_desc").GetText();
            Notification.ShowText(title, desc, Notification.TextType.EventText);
            queueJumper = null;

            return true;
        }

        public static bool PassRequestRequirement(bool log = true, int dayThresh = 0, int chapterThresh = 0, int popularityThresh = 0)
        {
            bool passed = true;
            string logMessage = "You are not ready to mess with the queue.";

            if (!PassDayRequirement(dayThresh))
            {
                passed = false;
                logMessage += " Wait a few days.";
            }

            if (!PassChapterRequirement(chapterThresh))
            {
                passed = false;
                logMessage += " Complete more of the alchemist\'s path.";
            }

            if (!PassPopularityRequirement(popularityThresh))
            {
                passed = false;
                logMessage += " Increase your popularity.";
            }

            if (log)
            {
                logSource.LogInfo(logMessage);
            }
            return passed;
        }

        public static bool PassDayRequirement(int requirement = 0)
        {
            return Managers.Day.CurrentDayAbsoluteNum >= requirement;
        }

        public static bool PassChapterRequirement(int requirement = 0)
        {
            return (Managers.Goals.GetCurrentChapterIndex(0, false) + 1) >= requirement;
        }

        public static bool PassPopularityRequirement(int requirement = 0)
        {
            return Managers.Player.popularity.Popularity >= requirement;
        }
    }
}
