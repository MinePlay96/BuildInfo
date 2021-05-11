﻿using System;
using System.Collections.Generic;
using System.Text;
using Digi.BuildInfo.Features.LiveData;
using Digi.BuildInfo.Utilities;
using Digi.BuildInfo.VanillaData;
using Digi.ComponentLib;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;
using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace Digi.BuildInfo.Features.Terminal
{
    public class TerminalInfo : ModComponent
    {
        public const int RefreshMinTicks = 15; // minimum amount of ticks between refresh calls
        private readonly string[] TickerText = { "––––––", "•–––––", "–•––––", "––•–––", "–––•––", "––––•–", "–––––•" };

        public List<IMyTerminalBlock> SelectedInTerminal = new List<IMyTerminalBlock>();
        private List<IMyTerminalBlock> SelectingList = new List<IMyTerminalBlock>();
        private HashSet<IMyTerminalBlock> SelectingSet = new HashSet<IMyTerminalBlock>();
        private IMyTerminalBlock LastSelected;

        public event Action SelectedChanged;

        public readonly HashSet<MyDefinitionId> IgnoreModBlocks = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);

        private IMyTerminalBlock viewedInTerminal;
        private int cursorCheckAfterTick = 0;
        private int refreshWaitForTick = 0;

        private int ticker;
        private int tickerUpdateAt;

        private readonly StringBuilder tmp = new StringBuilder(512);
        private readonly StringBuilder tmpInfo = new StringBuilder(512);

        private CustomInfoCall currentFormatCall;
        private delegate void CustomInfoCall(IMyTerminalBlock block, StringBuilder info);
        private readonly Dictionary<MyObjectBuilderType, CustomInfoCall> formatLookup
                   = new Dictionary<MyObjectBuilderType, CustomInfoCall>(MyObjectBuilderType.Comparer);

        private readonly HashSet<long> longSetTemp = new HashSet<long>();
        private readonly List<IMySlimBlock> nearbyBlocksCache = new List<IMySlimBlock>(); // list for reuse only

        private MyResourceSinkComponent _sinkCache = null;
        private MyResourceSourceComponent _sourceCache = null;
        private IMyInventory _invCache = null;
        private IMyInventory _inv2Cache = null;

        // on-demand self-caching component getters for the currently viewed block
        private MyResourceSinkComponent Sink => (_sinkCache = _sinkCache ?? viewedInTerminal.Components.Get<MyResourceSinkComponent>());
        private MyResourceSourceComponent Source => (_sourceCache = _sourceCache ?? viewedInTerminal.Components.Get<MyResourceSourceComponent>());
        private IMyInventory Inv => (_invCache = _invCache ?? viewedInTerminal.GetInventory(0));
        private IMyInventory Inv2 => (_inv2Cache = _inv2Cache ?? viewedInTerminal.GetInventory(1));

        private void ClearCaches()
        {
            _sinkCache = null;
            _sourceCache = null;
            _invCache = null;
            _inv2Cache = null;
        }

        public TerminalInfo(BuildInfoMod main) : base(main)
        {
        }

        public override void RegisterComponent()
        {
            RegisterFormats();

            MyAPIGateway.TerminalControls.CustomControlGetter += TerminalCustomControlGetter;
        }

        public override void UnregisterComponent()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter -= TerminalCustomControlGetter;
        }

        private void RegisterFormats()
        {
            CustomInfoCall action;

            action = Format_LightingBlock;
            Add(typeof(MyObjectBuilder_InteriorLight), action);
            Add(typeof(MyObjectBuilder_ReflectorLight), action);

            action = Format_Doors;
            Add(typeof(MyObjectBuilder_Door), action);
            Add(typeof(MyObjectBuilder_AdvancedDoor), action);
            Add(typeof(MyObjectBuilder_AirtightDoorGeneric), action);
            Add(typeof(MyObjectBuilder_AirtightHangarDoor), action);
            Add(typeof(MyObjectBuilder_AirtightSlideDoor), action);

            Add(typeof(MyObjectBuilder_CargoContainer), Format_CargoContainer);

            Add(typeof(MyObjectBuilder_ConveyorSorter), Format_ConveyorSorter);

            Add(typeof(MyObjectBuilder_ShipWelder), Format_ShipWelder);
            Add(typeof(MyObjectBuilder_ShipGrinder), Format_ShipGrinder);
            Add(typeof(MyObjectBuilder_Drill), Format_ShipDrill);

            action = Format_Piston;
            Add(typeof(MyObjectBuilder_PistonBase), action); // this one is actually ancient and unused?
            Add(typeof(MyObjectBuilder_ExtendedPistonBase), action);

            Add(typeof(MyObjectBuilder_ShipConnector), Format_Connector);

            action = Format_Rotor;
            Add(typeof(MyObjectBuilder_MotorAdvancedStator), action);
            Add(typeof(MyObjectBuilder_MotorStator), action);
            Add(typeof(MyObjectBuilder_MotorSuspension), action);

            Add(typeof(MyObjectBuilder_TimerBlock), Format_TimerBlock);

            Add(typeof(MyObjectBuilder_SoundBlock), Format_SoundBlock);

            Add(typeof(MyObjectBuilder_ButtonPanel), Format_ButtonPanel);

            action = Format_Weapons;
            Add(typeof(MyObjectBuilder_TurretBase), action);
            Add(typeof(MyObjectBuilder_ConveyorTurretBase), action);
            Add(typeof(MyObjectBuilder_SmallGatlingGun), action);
            Add(typeof(MyObjectBuilder_SmallMissileLauncher), action);
            Add(typeof(MyObjectBuilder_SmallMissileLauncherReload), action);
            Add(typeof(MyObjectBuilder_InteriorTurret), action);
            Add(typeof(MyObjectBuilder_LargeGatlingTurret), action);
            Add(typeof(MyObjectBuilder_LargeMissileTurret), action);

            // nothing useful to add, it also has a huge detail info text when a projection is loaded
            //Add(typeof(MyObjectBuilder_Projector),Format_Projector);
            //Add(typeof(MyObjectBuilder_ProjectorBase),Format_Projector);

            Add(typeof(MyObjectBuilder_OreDetector), Format_OreDetector);

            Add(typeof(MyObjectBuilder_Parachute), Format_Parachute);

            action = Format_GasTank;
            Add(typeof(MyObjectBuilder_GasTank), action);
            Add(typeof(MyObjectBuilder_OxygenTank), action);

            action = Format_Seats;
            Add(typeof(MyObjectBuilder_Cockpit), action);
            Add(typeof(MyObjectBuilder_CryoChamber), action);

            Add(typeof(MyObjectBuilder_RemoteControl), Format_RemoteControl);

            //Add(typeof(MyObjectBuilder_Gyro), Format_Gyro);

            Add(typeof(MyObjectBuilder_Thrust), Format_Thruster);

            Add(typeof(MyObjectBuilder_Collector), Format_Collector);

            Add(typeof(MyObjectBuilder_Reactor), Format_Reactor);
            Add(typeof(MyObjectBuilder_HydrogenEngine), Format_HydrogenEngine);
            Add(typeof(MyObjectBuilder_SolarPanel), Format_SolarPanel);
            Add(typeof(MyObjectBuilder_WindTurbine), Format_WindTurbine);

            action = Format_Production;
            Add(typeof(MyObjectBuilder_Refinery), action);
            Add(typeof(MyObjectBuilder_Assembler), action);
            Add(typeof(MyObjectBuilder_SurvivalKit), action);

            Add(typeof(MyObjectBuilder_UpgradeModule), Format_UpgradeModule);

            Add(typeof(MyObjectBuilder_MedicalRoom), Format_MedicalRoom);

            Add(typeof(MyObjectBuilder_OxygenGenerator), Format_GasGenerator);

            Add(typeof(MyObjectBuilder_OxygenFarm), Format_OxygenFarm);

            Add(typeof(MyObjectBuilder_AirVent), Format_AirVent);

            Add(typeof(MyObjectBuilder_RadioAntenna), Format_RadioAntenna);

            Add(typeof(MyObjectBuilder_LaserAntenna), Format_LaserAntenna);

            Add(typeof(MyObjectBuilder_Beacon), Format_Beacon);

            Add(typeof(MyObjectBuilder_MyProgrammableBlock), Format_ProgrammableBlock);

            //Add(typeof(MyObjectBuilder_JumpDrive), Format_JumpDrive);
        }

        private void Add(MyObjectBuilderType blockType, CustomInfoCall call)
        {
            formatLookup.Add(blockType, call);
        }

        public override void UpdateAfterSim(int tick)
        {
            if(LastSelected != null)
            {
                SelectedInTerminal.Clear();

                if(LastSelected != viewedInTerminal)
                {
                    ViewedBlockChanged(viewedInTerminal, LastSelected);

                    if(viewedInTerminal != null)
                    {
                        // HACK: required to avoid getting 2 blocks as selected when starting from a fast-refreshing block (e.g. airvent) and selecting a non-refreshing one (e.g. cargo container)
                        var orig = viewedInTerminal.ShowInToolbarConfig;
                        viewedInTerminal.ShowInToolbarConfig = !orig;
                        viewedInTerminal.ShowInToolbarConfig = orig;
                    }
                }

                LastSelected = null;

                MyUtils.Swap(ref SelectingList, ref SelectedInTerminal); // swap references, faster than adding to the 2nd list and clearing

                SelectingList.Clear();
                SelectingSet.Clear();

                SelectedChanged?.Invoke();
            }

            if(viewedInTerminal == null)
                return;

            // check if the block is still valid or if the player exited the menu
            // NOTE: IsCursorVisible reacts slowly to the menu being opened, an ignore period is needed
            if(viewedInTerminal.MarkedForClose || (cursorCheckAfterTick <= Main.Tick && !MyAPIGateway.Gui.IsCursorVisible))
            {
                SelectedInTerminal.Clear();
                ViewedBlockChanged(viewedInTerminal, null);
                return;
            }

            // only actively refresh if viewing the block list
            if(Main.Tick % RefreshMinTicks == 0
            && !Main.GUIMonitor.InAnyToolbarGUI
            && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
            {
                UpdateDetailInfo();
            }
        }

        // Gets called when local client clicks on a block in the terminal.
        // Used to know the currently viewed block in the terminal.
        void TerminalCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if(SelectingSet.Add(block))
            {
                SelectingList.Add(block);
            }

            LastSelected = block;

            UpdateMethods |= UpdateFlags.UPDATE_AFTER_SIM;
        }

        void ViewedBlockChanged(IMyTerminalBlock oldBlock, IMyTerminalBlock newBlock)
        {
            if(oldBlock != null)
            {
                oldBlock.AppendingCustomInfo -= CustomInfo;
                oldBlock.PropertiesChanged -= PropertiesChanged;
            }

            viewedInTerminal = null;
            currentFormatCall = null;

            fullScan = true;
            powerSourcesRecheckAfterTick = 0; // remove cooldown to instantly rescan
            powerSources.Clear();

            ClearCaches(); // block changed so caches are no longer relevant

            if(newBlock != null)
            {
                if(!formatLookup.TryGetValue(newBlock.BlockDefinition.TypeId, out currentFormatCall))
                    return; // ignore blocks that don't need stats

                viewedInTerminal = newBlock;

                cursorCheckAfterTick = Main.Tick + 10;

                newBlock.AppendingCustomInfo += CustomInfo;
                newBlock.PropertiesChanged += PropertiesChanged;

                UpdateDetailInfo(force: true);
            }

            SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, (viewedInTerminal != null));
        }

        void UpdateDetailInfo(bool force = false)
        {
            if(!force && !Main.Config.TerminalDetailInfoAdditions.Value)
                return;

            if(!force && refreshWaitForTick > Main.Tick)
                return;

            refreshWaitForTick = (Main.Tick + RefreshMinTicks);
            viewedInTerminal.RefreshCustomInfo();
        }

        void PropertiesChanged(IMyTerminalBlock block)
        {
            UpdateDetailInfo();
        }

        // Called by AppendingCustomInfo's invoker: RefreshCustomInfo()
        void CustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            try
            {
                if(currentFormatCall == null)
                    return;

                if(IgnoreModBlocks.Contains(block.BlockDefinition))
                    return;

                // Append other mod's info after my own.
                // This is possible since this event is surely executed last as it's hooked when block is clicked
                //   and because the same StringBuiler is given to all mods.
                string otherModInfo = (info.Length > 0 ? info.ToString() : null);

                info.Clear(); // mods shouldn't do this here, but I'm storing existing data and writing it back!

                bool addedCustomInfo = false;
                bool header = Main.Config.TerminalDetailInfoHeader.Value;

                if(Main.Config.TerminalDetailInfoAdditions.Value)
                {
                    if(header)
                    {
                        tmpInfo.Clear();
                        currentFormatCall.Invoke(block, tmpInfo);

                        if(tmpInfo.Length > 0)
                        {
                            addedCustomInfo = true;

                            string text = TickerText[ticker];

                            info.Append(text, 0, 3).Append("( BuildInfo | /bi )").Append(text, 3, 3).Append('\n');
                            info.AppendStringBuilder(tmpInfo);
                        }
                    }
                    else
                    {
                        currentFormatCall.Invoke(block, info);
                        addedCustomInfo = (info.Length > 0);
                    }

                    info.TrimEndWhitespace();
                }

                if(otherModInfo != null)
                {
                    if(addedCustomInfo)
                        info.NewLine();

                    info.Append(otherModInfo);
                }

                if(!header && addedCustomInfo)
                {
                    info.NewLine().Append(TickerText[ticker]);
                }

                if(tickerUpdateAt <= Main.Tick)
                {
                    ticker = (ticker + 1) % TickerText.Length;
                    tickerUpdateAt = Main.Tick + RefreshMinTicks;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
                info?.Append($"\n[ {Log.ModName} ERROR; SEND GAME LOG! ]");
            }
        }

        #region Text formatting per block type
        void Format_LightingBlock(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_Doors(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_OreDetector(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_TimerBlock(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.194.211:
            //      Time to trigger: 00:00:00

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_SoundBlock(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_CargoContainer(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            var cargoDef = (MyCargoContainerDefinition)block.SlimBlock.BlockDefinition;

            info.DetailInfo_Type(block);
            info.DetailInfo_Inventory(Inv, cargoDef.InventorySize.Volume);
        }

        void Format_ConveyorSorter(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Current Input: <n> W

            // Conveyor sorters can be used as a base block for WeaponCore.
            if(Main.WeaponCoreAPIHandler.Weapons.ContainsKey(block.BlockDefinition))
            {
                Format_WeaponCore(block, info);
                return;
            }

            var sorterDef = (MyConveyorSorterDefinition)block.SlimBlock.BlockDefinition;

            info.DetailInfo_MaxPowerUsage(Sink);
            info.DetailInfo_Inventory(Inv, sorterDef.InventorySize.Volume);
        }

        void Format_Connector(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);

            var def = (MyCubeBlockDefinition)block.SlimBlock.BlockDefinition;
            float volume;

            if(Utils.GetInventoryVolumeFromComponent(def, out volume))
            {
                info.DetailInfo_Inventory(Inv, volume);
            }
            else
            {
                info.DetailInfo_Inventory(Inv, Hardcoded.ShipConnector_InventoryVolume(def));
            }

            var data = Main.LiveDataHandler.Get<BData_Connector>(def);
            if(data != null && data.CanConnect)
            {
                var connector = (IMyShipConnector)block;

                if(connector.Status == MyShipConnectorStatus.Connectable)
                {
                    info.Append("Status: Ready to connect\n");
                    info.Append("Target: ").Append(connector.OtherConnector.CustomName).NewLine();
                    info.Append("Ship: ").Append(connector.OtherConnector.CubeGrid.CustomName).NewLine();
                }
                else if(connector.Status == MyShipConnectorStatus.Connected)
                {
                    info.Append("Status: Connected\n");
                    info.Append("Target: ").Append(connector.OtherConnector.CustomName).NewLine();
                    info.Append("Ship: ").Append(connector.OtherConnector.CubeGrid.CustomName).NewLine();
                }
                else
                {
                    info.Append("Status: Not connected\n");
                    info.Append("Target: ").Append("N/A").NewLine();
                    info.Append("Ship: ").Append("N/A").NewLine();
                }
            }
        }

        void Format_Weapons(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            if(Main.WeaponCoreAPIHandler.Weapons.ContainsKey(block.BlockDefinition))
            {
                Format_WeaponCore(block, info);
                return;
            }

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);

            var weaponBlockDef = block?.SlimBlock?.BlockDefinition as MyWeaponBlockDefinition;
            if(weaponBlockDef == null || Inv == null)
                return;

            float maxVolume;
            if(!Utils.GetInventoryVolumeFromComponent(weaponBlockDef, out maxVolume))
                maxVolume = weaponBlockDef.InventoryMaxVolume;

            info.DetailInfo_Inventory(Inv, maxVolume);

            var gun = block as IMyGunObject<MyGunBase>;
            if(gun?.GunBase?.CurrentAmmoMagazineDefinition == null || !gun.GunBase.HasAmmoMagazines)
                return;

            int loadedAmmo = gun.GunBase.CurrentAmmo;
            int mags = gun.GunBase.GetInventoryAmmoMagazinesCount();
            var magDef = gun.GunBase.CurrentAmmoMagazineDefinition;

            // assume one mag is loaded for simplicty sake
            if(loadedAmmo == 0 && mags > 0)
            {
                loadedAmmo = magDef.Capacity;
                mags -= 1;
            }

            info.Append("Ammo: ").Append(loadedAmmo).Append(" loaded + ").Append(mags * magDef.Capacity).Append(" in mags").NewLine();

            var weaponTracker = Main.ReloadTracking.WeaponLookup.GetValueOrDefault(block.EntityId, null);
            if(weaponTracker != null)
            {
                info.Append("Shots until reload: ");

                if(weaponTracker.ReloadUntilTick > 0)
                    info.Append("Reloading");
                else
                    info.Append(weaponTracker.ShotsUntilReload);

                info.Append(" / ").Append(weaponTracker.InternalMagazineCapacity).NewLine();
            }

            const int MaxMagNameLength = 26;

            var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponBlockDef.WeaponDefinitionId);

            if(weaponDef == null || weaponDef.AmmoMagazinesId.Length == 1)
            {
                info.Append("Type: ").AppendMaxLength(magDef.DisplayNameText, MaxMagNameLength).NewLine();
            }
            else
            {
                info.Append("Types:\n");
                info.Append(">").AppendMaxLength(magDef.DisplayNameText, MaxMagNameLength).NewLine();

                foreach(var otherMagId in weaponDef.AmmoMagazinesId)
                {
                    if(otherMagId == magDef.Id)
                        continue;

                    var otherMagDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(otherMagId);
                    if(otherMagDef != null)
                        info.Append("  ").AppendMaxLength(otherMagDef.DisplayNameText, MaxMagNameLength).NewLine();
                }
            }
        }

        void Format_WeaponCore(IMyTerminalBlock block, StringBuilder info)
        {
        }

        void Format_Production(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W
            //      Current Input: <n> W
            //      Productivity: <n>%
            //      Effectiveness: <n>%
            //      Power efficiency: <n>%
            //      Used upgrade module slots: <n> / <n>

            var productionDef = (MyProductionBlockDefinition)block.SlimBlock.BlockDefinition;
            var volume = (productionDef.InventoryMaxVolume > 0 ? productionDef.InventoryMaxVolume : productionDef.InventorySize.Volume);
            info.DetailInfo_Inventory(Inv, volume, "Inventory In");
            info.DetailInfo_Inventory(Inv2, volume, "Inventory Out");

            var production = (IMyProductionBlock)block;
            var assembler = block as IMyAssembler;
            var refinery = block as IMyRefinery;

            if(assembler != null)
            {
                info.Append("Mode: ");

                switch(assembler.Mode)
                {
                    case MyAssemblerMode.Assembly: info.Append("Assemble"); break;
                    case MyAssemblerMode.Disassembly: info.Append("Disassemble"); break;
                    default: info.Append(assembler.Mode.ToString()); break;
                }

                info.NewLine();
                info.Append("Loop queue: ").Append(assembler.Repeating ? "On" : "Off").NewLine();
            }

            if(production.IsQueueEmpty)
            {
                info.Append("Queue: (empty)\n");
            }
            else
            {
                info.Append("Queue: ").Append(production.IsProducing ? "Working..." : "STOPPED").NewLine();

                List<MyProductionQueueItem> queue = production.GetQueue(); // TODO avoid alloc here somehow?

                if(assembler != null || refinery != null)
                {
                    var assemblerDef = productionDef as MyAssemblerDefinition;
                    var refineryDef = productionDef as MyRefineryDefinition;

                    tmp.Clear();
                    float totalTime = 0;

                    for(int i = 0; i < queue.Count; ++i)
                    {
                        MyProductionQueueItem item = queue[i];
                        var bp = (MyBlueprintDefinitionBase)item.Blueprint;
                        float amount = (float)item.Amount;

                        float time = 0;
                        if(assembler != null)
                        {
                            time = Hardcoded.Assembler_BpProductionTime(bp, assemblerDef, assembler);
                            time *= amount;
                            totalTime += time;

                            // need access to MyAssembler.CurrentItemIndex to determine which queue item is actually being built

                            tmp.Append("• x").ShortNumber(amount);
                        }
                        else // refinery
                        {
                            time = Hardcoded.Refinery_BpProductionTime(bp, refineryDef, refinery);
                            time *= amount;
                            totalTime += time;

                            tmp.Append(i + 1).Append('.');

                            if(bp.Prerequisites.Length == 1 && bp.Results.Length == 1)
                            {
                                amount = (float)bp.Results[0].Amount / (float)bp.Prerequisites[0].Amount;
                                tmp.Append(" x").ShortNumber(amount);
                            }
                        }

                        tmp.Append(' ').Append(item.Blueprint.DisplayNameText).Append(" (").TimeFormat(time).Append(')').NewLine();
                    }

                    info.Append("Total time: ").TimeFormat(totalTime).NewLine();
                    info.AppendStringBuilder(tmp);
                    tmp.Clear();
                }
                else // unknown production block
                {
                    for(int i = 0; i < queue.Count; ++i)
                    {
                        MyProductionQueueItem item = queue[i];
                        float amount = (float)item.Amount;

                        info.Append("• x").Number(amount).Append(" ").Append(item.Blueprint.DisplayNameText).NewLine();
                    }
                }
            }
        }

        void Format_UpgradeModule(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);

            var upgradeModule = (IMyUpgradeModule)block;
            var def = (MyUpgradeModuleDefinition)upgradeModule.SlimBlock.BlockDefinition;

            if(def.Upgrades == null) // required as UpgradeCount throws NRE if block has no Upgrades tag at all (empty tag would be fine)
                return;

            if(upgradeModule.UpgradeCount == 0) // probably a platform for something else and not an actual upgrade module, therefore skip
                return;

            var upgradeModuleDef = (MyUpgradeModuleDefinition)block.SlimBlock.BlockDefinition;

            info.Append("Connections:");

            if(upgradeModule.Connections > 0)
            {
                info.NewLine();

                // since upgrade module doesn't expose what blocks it's connected to, I'll look for nearby blocks that have this upgrade module listed in their upgrades.
                longSetTemp.Clear();

                nearbyBlocksCache.Clear();
                upgradeModule.SlimBlock.GetNeighbours(nearbyBlocksCache);

                foreach(var nearSlim in nearbyBlocksCache)
                {
                    if(nearSlim?.FatBlock == null)
                        continue;

                    if(longSetTemp.Contains(nearSlim.FatBlock.EntityId)) // already processed this item
                        continue;

                    longSetTemp.Add(nearSlim.FatBlock.EntityId);

                    var nearCube = (MyCubeBlock)nearSlim.FatBlock;

                    if(nearCube.CurrentAttachedUpgradeModules == null)
                        continue;

                    foreach(var module in nearCube.CurrentAttachedUpgradeModules.Values)
                    {
                        if(module.Block == upgradeModule)
                        {
                            var name = ((nearCube as IMyTerminalBlock)?.CustomName ?? nearCube.DisplayNameText);
                            info.Append("• ").Append(module.SlotCount).Append("x ").Append(name);

                            if(!module.Compatible)
                                info.Append(" (incompatible)");

                            info.NewLine();
                            break;
                        }
                    }
                }

                nearbyBlocksCache.Clear();
                longSetTemp.Clear();
            }
            else
            {
                info.Append(" (none)\n");
            }

            info.Append("\nUpgrades per slot:\n");

            List<MyUpgradeModuleInfo> upgrades;
            upgradeModule.GetUpgradeList(out upgrades);

            foreach(var item in upgrades)
            {
                info.Append("• ").AppendUpgrade(item).NewLine();
            }
        }

        void Format_OxygenFarm(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W
            //      Oxygen output: <n> L/min

            info.DetailInfo_CurrentPowerUsage(Sink);
        }

        void Format_Seats(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //     [Main ship cockpit: <name>]

            var cockpit = (IMyCockpit)block;

            var def = (MyCockpitDefinition)cockpit.SlimBlock.BlockDefinition;
            if(Hardcoded.Cockpit_PowerRequired(def, cockpit.IsFunctional) > 0)
                info.DetailInfo_CurrentPowerUsage(Sink);

            if(cockpit.OxygenCapacity > 0)
                info.Append("Oxygen: ").ProportionToPercent(cockpit.OxygenFilledRatio).Append(" (").VolumeFormat(cockpit.OxygenCapacity * cockpit.OxygenFilledRatio).Append(" / ").VolumeFormat(cockpit.OxygenCapacity).Append(')').NewLine();

            Suffix_ShipController(block, info);
        }

        void Format_RemoteControl(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W

            info.DetailInfo_CurrentPowerUsage(Sink);

            Suffix_ShipController(block, info);
        }

        #region ShipController extra stuff
        void Suffix_ShipController(IMyTerminalBlock block, StringBuilder info)
        {
            info.DetailInfo_Inventory(Inv, Hardcoded.Cockpit_InventoryVolume);

            var blockToolbarData = Main.ToolbarCustomLabels.BlockData.GetValueOrDefault(block.EntityId, null);
            if(blockToolbarData != null && blockToolbarData.ParseErrors.Count > 0)
            {
                info.Append("\nToolbar CustomData Errors:");

                foreach(var line in blockToolbarData.ParseErrors)
                {
                    info.Append('\n').Append(line);
                }

                info.Append('\n');
            }

            var def = (MyShipControllerDefinition)block.SlimBlock.BlockDefinition;
            if(def.EnableShipControl)
            {
                var internalController = (MyShipController)block;
                var distributor = internalController.GridResourceDistributor;
                var state = distributor.ResourceStateByType(MyResourceDistributorComponent.ElectricityId);
                var required = distributor.TotalRequiredInputByType(MyResourceDistributorComponent.ElectricityId);
                var available = distributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId);

                FindPowerSources(block.CubeGrid);

                info.Append("\nShip power statistics:\n");
                info.Append("  Status: ");
                switch(state)
                {
                    case MyResourceStateEnum.NoPower: info.Append("No power!"); break;
                    case MyResourceStateEnum.Ok: info.Append("OK"); break;
                    case MyResourceStateEnum.OverloadAdaptible: info.Append("Minor Overload!"); break;
                    case MyResourceStateEnum.OverloadBlackout: info.Append("Heavy Overload!"); break;
                }
                info.NewLine();

                info.Append("  Total required: ").PowerFormat(required).NewLine();
                info.Append("  Total available: ").PowerFormat(available).NewLine();

                info.Append("  Reactors: ");
                if(reactors == 0)
                    info.Append("N/A\n");
                else
                    info.Append(reactorsWorking).Append(" of ").Append(reactors).Append(" working\n");

                info.Append("  Engines: ");
                if(engines == 0)
                    info.Append("N/A\n");
                else
                    info.Append(enginesWorking).Append(" of ").Append(engines).Append(" working\n");

                info.Append("  Batteries: ");
                if(batteries == 0)
                    info.Append("N/A\n");
                else
                    info.Append(batteriesWorking).Append(" of ").Append(batteries).Append(" working\n");

                info.Append("  Solar Panels: ");
                if(solarPanels == 0)
                    info.Append("N/A\n");
                else
                    info.Append(solarPanelsWorking).Append(" of ").Append(solarPanels).Append(" working\n");

                info.Append("  Wind Turbines: ");
                if(windTurbines == 0)
                    info.Append("N/A\n");
                else
                    info.Append(windTurbinesWorking).Append(" of ").Append(windTurbines).Append(" working\n");

                if(otherSources > 0)
                    info.Append("  Other power sources: ").Append(otherSourcesWorking).Append(" of ").Append(otherSources).Append(" working\n");
            }
        }

        private int powerSourcesRecheckAfterTick = 0;
        private bool fullScan = true;

        private int reactors = 0;
        private int reactorsWorking = 0;

        private int engines = 0;
        private int enginesWorking = 0;

        private int batteries = 0;
        private int batteriesWorking = 0;

        private int solarPanels = 0;
        private int solarPanelsWorking = 0;

        private int windTurbines = 0;
        private int windTurbinesWorking = 0;

        private int otherSources = 0;
        private int otherSourcesWorking = 0;

        private readonly List<IMyTerminalBlock> powerSources = new List<IMyTerminalBlock>();

        private void FindPowerSources(IMyCubeGrid grid)
        {
            if(grid == null || powerSourcesRecheckAfterTick > Main.Tick)
                return;

            powerSourcesRecheckAfterTick = Constants.TICKS_PER_SECOND * 3;

            if(fullScan)
            {
                FindPowerSources_FullScan(grid);
            }
            else
            {
                FindPowerSources_UpdateWorking(grid);
            }
        }

        private void FindPowerSources_FullScan(IMyCubeGrid grid)
        {
            reactors = 0;
            reactorsWorking = 0;
            engines = 0;
            enginesWorking = 0;
            batteries = 0;
            batteriesWorking = 0;
            solarPanels = 0;
            solarPanelsWorking = 0;
            windTurbines = 0;
            windTurbinesWorking = 0;
            otherSources = 0;
            otherSourcesWorking = 0;

            powerSources.Clear();

            var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if(gts != null)
                gts.GetBlocksOfType(powerSources, FindPowerSources_ComputeBlock);
        }

        private bool FindPowerSources_ComputeBlock(IMyTerminalBlock block)
        {
            var source = block.Components?.Get<MyResourceSourceComponent>();
            if(source == null)
                return false;

            foreach(var res in source.ResourceTypes)
            {
                if(res == MyResourceDistributorComponent.ElectricityId)
                {
                    bool working = (source.CurrentOutputByType(MyResourceDistributorComponent.ElectricityId) > 0);

                    if(block is IMyReactor)
                    {
                        reactors++;

                        if(working)
                            reactorsWorking++;
                    }
                    else if(block.BlockDefinition.TypeId == typeof(MyObjectBuilder_HydrogenEngine)) // TODO: use the interface when one is added
                    {
                        engines++;

                        if(working)
                            enginesWorking++;
                    }
                    else if(block is IMyBatteryBlock)
                    {
                        batteries++;

                        if(working)
                            batteriesWorking++;
                    }
                    else if(block is IMySolarPanel)
                    {
                        solarPanels++;

                        if(working)
                            solarPanelsWorking++;
                    }
                    else if(block.BlockDefinition.TypeId == typeof(MyObjectBuilder_WindTurbine)) // TODO: use the interface when one is added
                    {
                        windTurbines++;

                        if(working)
                            windTurbinesWorking++;
                    }
                    else
                    {
                        otherSources++;

                        if(working)
                            otherSourcesWorking++;
                    }

                    return true;
                }
            }

            return false;
        }

        private void FindPowerSources_UpdateWorking(IMyCubeGrid grid)
        {
            reactorsWorking = 0;
            enginesWorking = 0;
            batteriesWorking = 0;
            solarPanelsWorking = 0;
            windTurbinesWorking = 0;
            otherSourcesWorking = 0;

            foreach(var block in powerSources)
            {
                var source = block.Components?.Get<MyResourceSourceComponent>();
                if(source == null)
                    continue;

                bool working = (source.CurrentOutputByType(MyResourceDistributorComponent.ElectricityId) > 0);
                if(!working)
                    continue;

                if(block is IMyReactor)
                    reactorsWorking++;
                else if(block.BlockDefinition.TypeId == typeof(MyObjectBuilder_HydrogenEngine)) // TODO: use the interface when one is added
                    enginesWorking++;
                else if(block is IMyBatteryBlock)
                    batteriesWorking++;
                else if(block is IMySolarPanel)
                    solarPanelsWorking++;
                else if(block.BlockDefinition.TypeId == typeof(MyObjectBuilder_WindTurbine)) // TODO: use the interface when one is added
                    windTurbinesWorking++;
                else
                    otherSourcesWorking++;
            }
        }
        #endregion ShipController extra stuff

        void Format_Rotor(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Current angle or status

            info.DetailInfo_InputPower(Sink);

            var rotorStator = block as IMyMotorStator;
            if(rotorStator != null)
            {
                float mass;
                float pickedTorque = rotorStator.Torque;
                float realTorque = Hardcoded.RotorTorqueLimit(rotorStator, out mass);

                if(realTorque != pickedTorque)
                {
                    info.NewLine().Append("Capped Torque: ").TorqueFormat(realTorque).Append(" ([4] @ /bi)").NewLine();
                }

                if(mass > 0)
                {
                    if(realTorque == pickedTorque)
                        info.NewLine();

                    info.Append("Attached Mass: ").MassFormat(mass).NewLine();
                }

                if(Main.Config.InternalInfo.Value)
                {
                    info.NewLine().Append("API Angle: ").RoundedNumber(rotorStator.Angle, 2).Append(" radians").NewLine();
                }
            }
        }

        void Format_Piston(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Current position

            info.DetailInfo_InputPower(Sink);

            if(Main.Config.InternalInfo.Value)
            {
                var piston = (IMyPistonBase)block;
                info.Append("API Position: ").DistanceFormat(piston.CurrentPosition, 5).NewLine();
            }
        }

        void Format_AirVent(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.198:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W
            //      Room pressure: <status>

            var sink = Sink;
            info.DetailInfo_CurrentPowerUsage(sink);

            var airVent = (IMyAirVent)block;
            if(airVent.Depressurize)
            {
                info.Append("Depressurizing:\n");
                info.DetailInfo_OutputGasList(Source, "  ");
            }
            else
            {
                info.Append("Pressurizing:\n");
                info.DetailInfo_InputGasList(sink, "  ");
            }
        }

        void Format_Reactor(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Output: <n> W
            //      Current Output: <n> W

            if(Inv == null)
                return;

            var reactorDef = (MyReactorDefinition)block.SlimBlock.BlockDefinition;

            if(reactorDef.FuelInfos != null && reactorDef.FuelInfos.Length > 0)
            {
                var ratio = Source.CurrentOutput / reactorDef.MaxPowerOutput;

                if(reactorDef.FuelInfos.Length == 1)
                {
                    var fuel = reactorDef.FuelInfos[0];
                    float perSec = ratio * fuel.ConsumptionPerSecond_Items;
                    float seconds = (perSec > 0 ? ((float)Inv.CurrentMass / perSec) : 0);

                    info.Append("Current Usage: ").MassFormat(perSec).Append("/s").NewLine();
                    info.Append("Time Left: ").TimeFormat(seconds).NewLine();
                    info.Append("Uses Fuel: ").IdTypeSubtypeFormat(fuel.FuelId).NewLine();
                }
                else
                {
                    tmp.Clear();
                    float perSec = 0;

                    foreach(var fuel in reactorDef.FuelInfos)
                    {
                        tmp.Append("  ").IdTypeSubtypeFormat(fuel.FuelId).Append(" (").MassFormat(fuel.ConsumptionPerSecond_Items).Append("/s)").NewLine();

                        perSec += ratio * fuel.ConsumptionPerSecond_Items;
                    }

                    float seconds = (perSec > 0 ? ((float)Inv.CurrentMass / perSec) : 0);

                    info.Append("Current Usage: ").MassFormat(perSec).Append("/s").NewLine();
                    info.Append("Time Left: ").TimeFormat(seconds).NewLine();
                    info.Append("Uses Combined Fuels: ").NewLine();
                    info.AppendStringBuilder(tmp);
                    tmp.Clear();
                }
            }

            float maxVolume;
            if(!Utils.GetInventoryVolumeFromComponent(reactorDef, out maxVolume))
                maxVolume = (reactorDef.InventoryMaxVolume > 0 ? reactorDef.InventoryMaxVolume : reactorDef.InventorySize.Volume);

            info.DetailInfo_Inventory(Inv, maxVolume);
        }

        void Format_HydrogenEngine(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Output: 5.00 MW
            //      Current Output: 0 W
            //      Filled: 0.0% (0L/500000L)

            info.DetailInfo_InputHydrogen(Sink);
        }

        void Format_WindTurbine(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Output: 5.00 MW
            //      Current Output: 0 W
            //      Wind Clearance: <Text>

            var turbineDef = (MyWindTurbineDefinition)block.SlimBlock.BlockDefinition;

            float optimalOutput = turbineDef.MaxPowerOutput; // * ((turbineDef.RaycasterSize - 1) * turbineDef.RaycastersToFullEfficiency);

            info.Append("Optimal Output: ").PowerFormat(optimalOutput).NewLine();

            var grid = block.CubeGrid;
            Vector3D position = grid.Physics?.CenterOfMassWorld ?? grid.PositionComp.GetPosition();
            var planet = MyGamePruningStructure.GetClosestPlanet(position);

            info.Append("Current wind speed: ");
            if(planet != null && planet.PositionComp.WorldAABB.Contains(position) != ContainmentType.Disjoint)
            {
                float windSpeed = planet.GetWindSpeed(position) * MyAPIGateway.Session.WeatherEffects.GetWindMultiplier(position);
                info.Append(windSpeed.ToString("0.##"));
            }
            else
            {
                info.Append("N/A");
            }
        }

        void Format_SolarPanel(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Output: 5.00 MW
            //      Current Output: 0 W

            var solarDef = (MySolarPanelDefinition)block.SlimBlock.BlockDefinition;
            info.Append("Max Possible Output: ").PowerFormat(solarDef.MaxPowerOutput).NewLine();
        }

        //void Format_Gyro(IMyTerminalBlock block, StringBuilder info)
        //{
        //    // Vanilla info in 1.198.027:
        //    //      Type: <BlockDefName>
        //    //      Max Required Input: <n> W
        //
        //}

        void Format_Thruster(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W

            var thrust = (IMyThrust)block;
            var thrustInfo = Hardcoded.Thrust_GetUsage(thrust);
            var def = thrustInfo.Def;

            if(thrustInfo.Fuel != MyResourceDistributorComponent.ElectricityId)
            {
                info.Append("Requires: ").Append(thrustInfo.Fuel.SubtypeName).NewLine();

                info.Append("Current Usage: ").VolumeFormat(thrustInfo.CurrentUsage).Append("/s");
                if(Math.Abs(thrustInfo.CurrentConsumptionMul - 1) > 0.001f)
                    info.Append(" (x").RoundedNumber(thrustInfo.CurrentConsumptionMul, 2).Append(")");
                info.NewLine();

                info.Append("Max Usage: ").VolumeFormat(thrustInfo.MaxUsage).Append("/s");
                if(Math.Abs(thrustInfo.EarthConsumpationMul - 1) > 0.001f)
                    info.Append(" (x").RoundedNumber(thrustInfo.EarthConsumpationMul, 2).Append(" @ 1g)");
                info.NewLine();
            }
            else
            {
                info.Append("Requires: Electricity").NewLine();

                info.Append("Current Usage: ").PowerFormat(thrustInfo.CurrentUsage);
                if(Math.Abs(thrustInfo.CurrentConsumptionMul - 1) > 0.001f)
                    info.Append(" (x").RoundedNumber(thrustInfo.CurrentConsumptionMul, 2).Append(")");
                info.NewLine();

                info.Append("Max Usage: ").PowerFormat(thrustInfo.MaxUsage);
                if(Math.Abs(thrustInfo.EarthConsumpationMul - 1) > 0.001f)
                    info.Append(" (x").RoundedNumber(thrustInfo.EarthConsumpationMul, 2).Append(" @ 1g)");
                info.NewLine();
            }

            info.NewLine();

            // HACK NOTE: def.NeedsAtmosphereForInfluence does nothing, influence is always air density
            if(def.EffectivenessAtMinInfluence < 1.0f || def.EffectivenessAtMaxInfluence < 1.0f)
            {
                // renamed to what they actually are for simpler code
                float minAir = def.MinPlanetaryInfluence;
                float maxAir = def.MaxPlanetaryInfluence;
                float thrustAtMinAir = def.EffectivenessAtMinInfluence;
                float thrustAtMaxAir = def.EffectivenessAtMaxInfluence;

                info.Append("Current Max Thrust: ").ForceFormat(thrust.MaxEffectiveThrust).NewLine();
                info.Append("Optimal Max Thrust: ").ForceFormat(thrust.MaxThrust).NewLine();
                info.Append("Limits:").NewLine();

                // if mod has weird values, can't really present them in an understandable manner so just printing the values instead
                if(!Hardcoded.Thrust_HasSaneLimits(def))
                {
                    info.Append(" Min air density: ").ProportionToPercent(minAir).NewLine();
                    info.Append(" Max air density: ").ProportionToPercent(maxAir).NewLine();
                    info.Append(" Thrust at min air: ").ProportionToPercent(thrustAtMinAir).NewLine();
                    info.Append(" Thrust at max air: ").ProportionToPercent(thrustAtMaxAir).NewLine();

                    if(def.NeedsAtmosphereForInfluence)
                        info.Append(" No atmosphere causes 'thrust at min air'.").NewLine();
                }
                else
                {
                    info.Append("  ").ProportionToPercent(thrustAtMaxAir).Append(" thrust ");
                    if(maxAir <= 0f)
                        info.Append("in vacuum.");
                    else if(maxAir < 1f)
                        info.Append("in ").ProportionToPercent(maxAir).Append(" air density.");
                    else
                        info.Append("in atmosphere.");
                    info.NewLine();

                    info.Append("  ").ProportionToPercent(thrustAtMinAir).Append(" thrust ");
                    if(def.NeedsAtmosphereForInfluence || minAir <= 0f)
                        info.Append("in vacuum.");
                    else
                        info.Append("below ").ProportionToPercent(minAir).Append(" air density.");
                    info.NewLine();
                }
            }
            else
            {
                info.Append("Max Thrust: ").ForceFormat(thrust.MaxThrust).NewLine();
                info.Append("No atmosphere or vacuum limits.");
            }
        }

        void Format_RadioAntenna(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Current Input: <n> W

            var def = (MyRadioAntennaDefinition)block.SlimBlock.BlockDefinition;

            info.Append("Max Power Usage: ").PowerFormat(Hardcoded.RadioAntenna_PowerReq(def.MaxBroadcastRadius)).NewLine();
        }

        void Format_LaserAntenna(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Current Input: <n> W
            //      <LaserAntennaStatus>

            var antenna = (IMyLaserAntenna)block;
            var def = (MyLaserAntennaDefinition)block.SlimBlock.BlockDefinition;

            info.Append("Power Usage:\n");

            info.Append("  Current: ").PowerFormat(Sink.CurrentInputByType(MyResourceDistributorComponent.ElectricityId)).NewLine();

            info.Append("  At Range: ");
            if(antenna.Range < 1E+08f)
                info.PowerFormat(Hardcoded.LaserAntenna_PowerUsage(def, antenna.Range));
            else
                info.Append("Infinite.");
            info.NewLine();

            info.Append("  Max: ");
            if(def.MaxRange > 0)
                info.PowerFormat(Hardcoded.LaserAntenna_PowerUsage(def, def.MaxRange));
            else
                info.Append("Infinite.");
            info.NewLine();
        }

        void Format_Beacon(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.194.211:
            //      Type: <BlockDefName>
            //      Current Input: <n> W

            var def = (MyBeaconDefinition)block.SlimBlock.BlockDefinition;

            info.Append("Max Power Usage: ").PowerFormat(Hardcoded.Beacon_PowerReq(def)).NewLine();
        }

        void Format_GasGenerator(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W

            info.DetailInfo_CurrentPowerUsage(Sink);
            info.DetailInfo_OutputGasList(Source);
        }

        void Format_GasTank(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      Type: <BlockDefName>
            //      Max Required Input: <n> W
            //      Filled: <n>% (<current>L / <max>L)

            info.DetailInfo_CurrentPowerUsage(Sink);
            info.DetailInfo_InputGasList(Sink);
            info.DetailInfo_OutputGasList(Source);
        }

        void Format_MedicalRoom(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_ShipWelder(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.194.211:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
            info.DetailInfo_Inventory(Inv);
        }

        void Format_ShipGrinder(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.194.211:
            //      (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
            info.DetailInfo_Inventory(Inv);
        }

        void Format_ShipDrill(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.194.211:
            //      Type: <BlockDefName>
            //      Max Required Input: <Power>

            info.DetailInfo_InputPower(Sink);
            info.DetailInfo_Inventory(Inv);
        }

        void Format_Parachute(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //     (nothing)

            var parachute = (IMyParachute)block;

            info.DetailInfo_Type(block);
            info.DetailInfo_Inventory(Inv);
            info.Append("Atmosphere density: ").ProportionToPercent(parachute.Atmosphere).NewLine();
        }

        void Format_Collector(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //     (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
            info.DetailInfo_Inventory(Inv);
        }

        void Format_ButtonPanel(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041:
            //     (nothing)

            info.DetailInfo_Type(block);
            info.DetailInfo_InputPower(Sink);
        }

        void Format_ProgrammableBlock(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.189.041 (and probably forever):
            //     (nothing)

            if(!MyAPIGateway.Session.SessionSettings.EnableIngameScripts)
            {
                info.Append("In-game Scripts are disabled in world settings!\n");
                return;
            }

            string echoText = block.DetailedInfo;
            if(!string.IsNullOrEmpty(echoText))
                return; // only print something if PB itself doesn't

            if(MyAPIGateway.Session.SessionSettings.EnableScripterRole && MyAPIGateway.Session?.Player != null && MyAPIGateway.Session.Player.PromoteLevel < MyPromoteLevel.Scripter)
            {
                info.Append("Scripter role is required to use in-game scripts.\n");
            }

            if(!block.GetPlayerRelationToOwner().IsFriendly())
                return;

            PBData pbd;
            if(Main.PBMonitor.PBData.TryGetValue(block.EntityId, out pbd))
            {
                float sec = (float)Math.Round((Main.Tick - pbd.SavedAtTick) / 60f);
                info.Append("(BuildInfo: text from ").TimeFormat(sec).Append(" ago)").NewLine();
                info.NewLine();
                info.Append(pbd.EchoText).NewLine();
            }
        }

        // TODO: finish jumpdrive terminal info when jump destination can be retrieved
#if false
        void Format_JumpDrive(IMyTerminalBlock block, StringBuilder info)
        {
            // Vanilla info in 1.197.181:
            //      Type: <BlockDefName>
            //      Max Required Input: <Power>
            //      Max Stored Power: <Power>
            //      Current Input: <Power>
            //      Stored Power: <Power>
            //      Fully recharged in: <Time>
            //      Max jump distance: <Distance>
            //      [Current jump: <Percent>] (if GPS selected)

            var jumpDrive = block as MyJumpDrive;
            if(jumpDrive == null)
                return;

            var player = MyAPIGateway.Session?.Player;
            if(player == null)
                return;

            float powerCost = 0f;
            double distance = GetSelectedJumpDistance(jumpDrive);

            // from MyGridJumpDriveSystem.DepleteJumpDrives()
            double mass = jumpDrive.CubeGrid.GetCurrentMass();

            // DEBUG TODO: optimize
            foreach(var b in jumpDrive.CubeGrid.GetFatBlocks())
            {
                var jd = b as MyJumpDrive;
                if(jd == null || !jd.CanJumpAndHasAccess(player.IdentityId))
                    continue;

                double massRatio = Math.Min(jd.BlockDefinition.MaxJumpMass / mass, 1.0);
                double jumpDistance = jd.BlockDefinition.MaxJumpDistance * massRatio;

                if(jumpDistance < distance)
                {
                    distance -= jumpDistance;
                    if(b == block)
                    {
                        powerCost = jd.BlockDefinition.RequiredPowerInput;
                        break; // don't care about the rest of the blocks
                    }
                }
                else
                {
                    if(b == block)
                    {
                        double ratio = distance / jumpDistance;
                        powerCost = jd.BlockDefinition.RequiredPowerInput * (float)ratio;
                    }
                    break; // this is where game stops it
                }
            }

            info.Append("Jump Drain: ").PowerStorageFormat(powerCost);
        }

        double GetSelectedJumpDistance(MyJumpDrive jumpDrive)
        {
            var tb = (IMyTerminalBlock)jumpDrive;
            var prop = (IMyTerminalControl)tb.GetProperty("JumpDistance");
            bool hasJumpTarget = prop.Enabled.Invoke(jumpDrive);

            if(hasJumpTarget)
            {
                // DEBUG TODO ...
                return 5000;
            }
            else
            {
                return ComputeMaxDistance(jumpDrive);
            }
        }

        double ComputeMaxDistance(MyJumpDrive jumpDrive)
        {
            double maxJumpDistance = GetMaxJumpDistance(jumpDrive, jumpDrive.OwnerId);
            if(maxJumpDistance < 5000.0)
                return 5000.0;

            float jumpDistanceRatio = jumpDrive.GetValue<float>("JumpDistance");
            return 5001.0 + (maxJumpDistance - 5000.0) * jumpDistanceRatio;
        }

        double GetMaxJumpDistance(MyJumpDrive jumpDrive, long userId)
        {
            double absoluteMaxDistance = 0.0;
            double maxDistance = 0.0;
            double mass = jumpDrive.CubeGrid.GetCurrentMass();

            // DEBUG TODO: optimize
            foreach(var b in jumpDrive.CubeGrid.GetFatBlocks())
            {
                var jd = b as MyJumpDrive;
                if(jd == null || !jd.CanJumpAndHasAccess(userId))
                    continue;

                absoluteMaxDistance += jd.BlockDefinition.MaxJumpDistance;
                maxDistance += jd.BlockDefinition.MaxJumpDistance * (jd.BlockDefinition.MaxJumpMass / mass);
            }

            return Math.Min(absoluteMaxDistance, maxDistance);
        }
#endif

        #endregion Text formatting per block type
    }
}