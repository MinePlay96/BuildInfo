﻿using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Digi.BuildInfo.Features
{
    public class BackpackBarStat : IMyHudStat
    {
        public const string GroupName = "Cargo";
        public const int UpdateFrequencyTicks = (int)(Constants.TICKS_PER_SECOND * 1.0);
        public const string TextFormat = "###,###,###,###,##0.##";

        public MyStringHash Id { get; private set; }
        public float CurrentValue { get; private set; }
        public float MinValue => 0;
        public float MaxValue { get; private set; }
        public string GetValueString()
        {
            bool enabled = BuildInfoMod.Instance.Config.InventoryBarOverride.Value;
            if(!enabled)
                return CurrentValue.ToString(TextFormat);

            // TODO toggle string formatting?
            if(WasInShip)
            {
                if(UsingGroup)
                    return $"{Containers.ToString()} containers: {CurrentValue.ToString(TextFormat)} / {MaxValue.ToString(TextFormat)}";
                else
                    return $"'{GroupName}' group: {CurrentValue.ToString(TextFormat)} / {MaxValue.ToString(TextFormat)}";
            }
            else
                return $"Backpack: {CurrentValue.ToString(TextFormat)} / {MaxValue.ToString(TextFormat)}";
        }

        int Containers = 0;
        bool WasInShip = false;
        bool UsingGroup;

        public BackpackBarStat()
        {
            Id = MyStringHash.GetOrCompute("player_inventory_capacity"); // overwrites this stat's script
        }

        public void Update()
        {
            bool enabled = BuildInfoMod.Instance.Config.InventoryBarOverride.Value;

            if(enabled && MyAPIGateway.Session?.ControlledObject == null)
            {
                CurrentValue = 0f;
                MaxValue = 0f;
                Containers = 0;
                WasInShip = false;
                return;
            }

            var controlled = (enabled ? MyAPIGateway.Session.ControlledObject as IMyTerminalBlock : null);
            if(controlled != null)
            {
                if(!WasInShip || BuildInfoMod.Instance.Tick % UpdateFrequencyTicks == 0)
                {
                    CurrentValue = 0f;
                    MaxValue = 0f;
                    Containers = 0;
                    WasInShip = true;

                    float currentTotal = 0;
                    float maxTotal = 0;

                    var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(controlled.CubeGrid);
                    if(gts == null)
                        return;

                    var blocks = BuildInfoMod.Instance.ShipToolInventoryBar.Blocks;
                    blocks.Clear();

                    var group = gts.GetBlockGroupWithName(GroupName);
                    UsingGroup = (group != null);
                    if(UsingGroup)
                        group.GetBlocks(blocks);
                    else
                        gts.GetBlocksOfType<IMyCargoContainer>(blocks);

                    foreach(var block in blocks)
                    {
                        //if(!UsingGroup && !(block is IMyCargoContainer || block is IMyShipConnector || block is IMyCollector))
                        //    continue;

                        if(!block.IsSameConstructAs(controlled))
                            continue;

                        Containers++;

                        for(int i = 0; i < block.InventoryCount; i++)
                        {
                            var inv = block.GetInventory(i);
                            currentTotal += (float)inv.CurrentVolume * 1000; // add as liters
                            maxTotal += (float)inv.MaxVolume * 1000;
                        }
                    }

                    blocks.Clear();

                    CurrentValue = currentTotal;
                    MaxValue = maxTotal;
                }
            }
            else
            {
                if(WasInShip || BuildInfoMod.Instance.Tick % 10 == 0)
                {
                    WasInShip = false;

                    IMyInventory inventory = MyAPIGateway.Session?.Player?.Character?.GetInventory();
                    if(inventory != null)
                    {
                        Containers = 1;
                        MaxValue = MyFixedPoint.MultiplySafe(inventory.MaxVolume, 1000).ToIntSafe();
                        CurrentValue = MyFixedPoint.MultiplySafe(inventory.CurrentVolume, 1000).ToIntSafe();
                    }
                }
            }
        }
    }
}