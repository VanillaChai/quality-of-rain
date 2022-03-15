﻿using BepInEx;
using BepInEx.Configuration;
using R2API.Utils;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Chai
{
	[BepInDependency(R2API.R2API.PluginGUID)]
	[BepInPlugin(PluginGUID, PluginName, PluginVersion)]

	public class QualityOfRain : BaseUnityPlugin
	{
		public const string PluginGUID = "com.chai.qualityOfRain";
		public const string PluginAuthor = "Chai";
		public const string PluginName = "Quality of Rain";
		public const string PluginVersion = "1.1.0";

		private const float RADIUS = 7f;
		private const float BIG_RADIUS = 11.5f;

		private static ConfigFile ConfigFile { get; set; }
		public static ConfigEntry<bool> FastPrinters { get; set; }
		public static ConfigEntry<bool> FastShrineOfChance { get; set; }
		public static ConfigEntry<bool> FastScrappers { get; set; }
		public static ConfigEntry<bool> FastCauldrons { get; set; }
		public static ConfigEntry<bool> ShareLunarCoins { get; set; }
		public static ConfigEntry<bool> TeleportGunnerTurrets { get; set; }

		public void Awake()
		{
			// Create config
			ConfigFile = new ConfigFile(Paths.ConfigPath + "\\Quality of Rain.cfg", true);

			FastPrinters = ConfigFile.Bind(
				"Settings", "Fast Printers", true,
				"Make printers print instantly."
			);
			FastShrineOfChance = ConfigFile.Bind(
				"Settings", "Fast Shrine of Chance", true,
				"Remove the usage delay from shrine of chances."
			);
			FastScrappers = ConfigFile.Bind(
				"Settings", "Fast Scrappers", true,
				"Make scrappers scrap very fast."
			);
			FastCauldrons = ConfigFile.Bind(
				"Settings", "Fast Cauldrons", true,
				"Make cauldrons bubble instantly."
			);
			ShareLunarCoins = ConfigFile.Bind(
				"Settings", "Share Lunar Coins", true,
				"Everyone will get a lunar coin when one is picked up."
			);
			TeleportGunnerTurrets = ConfigFile.Bind(
				"Settings", "Teleport Gunner Turrets", true,
				"Gunner turrets will be teleported to the teleporter when it is activated."
			);

			// Remove delays from some interactables
			On.RoR2.Stage.Start += (orig, self) =>
			{
				orig(self);
				if (NetworkServer.active)
				{
					if (FastPrinters.Value)
					{
						typeof(EntityStates.Duplicator.Duplicating).SetFieldValue("initialDelayDuration", 0f);
						typeof(EntityStates.Duplicator.Duplicating).SetFieldValue("timeBetweenStartAndDropDroplet", 0f);
					}

					if (FastScrappers.Value)
					{
						//typeof(EntityStates.Scrapper.WaitToBeginScrapping).SetFieldValue("duration", 0f);
						typeof(EntityStates.Scrapper.Scrapping).SetFieldValue("duration", 0f);
						typeof(EntityStates.Scrapper.ScrappingToIdle).SetFieldValue("duration", 0.25f);
					}
				}
			};

			// Remove sound and animation if host
			On.EntityStates.Duplicator.Duplicating.BeginCooking += (orig, self) =>
			{
				if (!FastPrinters.Value || !NetworkServer.active) { orig(self); }
			};

			// Let clients know duplicator can be used
			On.EntityStates.Duplicator.Duplicating.DropDroplet += (orig, self) =>
			{
				orig(self);
				if (FastPrinters.Value && NetworkServer.active)
				{
					self.outer.GetComponent<PurchaseInteraction>().Networkavailable = true;
				}
			};

			// Remove shrine of chance cooldown
			On.RoR2.ShrineChanceBehavior.AddShrineStack += (orig, self, activator) =>
			{
				orig(self, activator);
				if (FastShrineOfChance.Value && NetworkServer.active)
				{
					self.SetFieldValue("refreshTimer", 0f);
				}
			};

			// Make cauldrons instant
			On.RoR2.EntityLogic.DelayedEvent.CallDelayed += (orig, self, timer) =>
			{
				if (FastCauldrons.Value && NetworkServer.active && self.ToString().Contains("LunarCauldron"))
				{
					timer = 0f;
				}
				
				orig(self, timer);
			};

			// Share lunar coins will all players
			On.RoR2.GenericPickupController.GrantLunarCoin += (orig, self, body, count) =>
			{
				orig(self, body, count);

				if (ShareLunarCoins.Value && NetworkServer.active)
				{
					foreach (CharacterMaster cm in CharacterMaster.readOnlyInstancesList)
					{
						NetworkUser networkUser = Util.LookUpBodyNetworkUser(cm.GetBody());
						if (cm.GetBody() == body || networkUser == null) { continue; }
						networkUser.AwardLunarCoins(count);
					}
				}
			};

			On.RoR2.UI.PickupPickerPanel.Awake += (orig, self) =>
			{
				orig.Invoke(self);
				Time.timeScale = 0f;
			};
			On.RoR2.PickupPickerController.OnDisplayEnd += (orig, self, NetworkUIPromptController, LocalUser, CameraRigController) =>
			{
				orig.Invoke(self, NetworkUIPromptController, LocalUser, CameraRigController);
				//LocalUser.cachedBody.AddTimedBuff(BuffCatalog.FindBuffIndex("HiddenInvincibility"), 3);
				//LocalUser.cachedBody.AddTimedBuff(BuffCatalog.FindBuffIndex("HealingDisabled"), 3);
				Time.timeScale = 1f;
			};

			/*On.RoR2.UI.PickupPickerPanel.SetPickupOptions += (orig, self, options) =>
			{
				ChatMessage.Send("panel");
				orig(self, options);
			};*/

			// Teleport all gunner turrets to teleporter
			On.RoR2.TeleporterInteraction.OnInteractionBegin += (orig, self, activator) =>
			{
				orig(self, activator);
				if (!TeleportGunnerTurrets.Value || !NetworkServer.active || !self.isIdle) { return; }

				List<CharacterMaster> turrets = new List<CharacterMaster>();

				foreach (CharacterMaster cm in CharacterMaster.readOnlyInstancesList)
				{
					if (cm.name.StartsWith("Turret1"))
					{
						turrets.Add(cm);
					}
				}

				float angle = 2 * Mathf.PI / turrets.Count;
				bool primordial = self.name.StartsWith("Lunar");
				float radius = primordial ? BIG_RADIUS : RADIUS;
				for (int i = 0; i < turrets.Count; ++i)
				{
					Vector3 point = new Vector3(Mathf.Cos(angle * i) * radius, 10f, Mathf.Sin(angle * i) * radius);

					if (Physics.Raycast(self.transform.position + point, Vector3.down, out RaycastHit hit))
					{
						point = hit.point;
						point.y += 0.1f;
						TeleportHelper.TeleportBody(turrets[i].GetBody(), point);
					}
				}
			};
		}

		// Debug code
		/*void Update()
		{
			if (Input.GetKeyDown(KeyCode.F2))
			{
				var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;
				PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex("LunarCoin.Coin0"), transform.position, transform.forward * 20f);
			}
		}*/
	}
}
