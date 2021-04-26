﻿using System.Collections.Generic;
using System.Text;
using Digi.BuildInfo.Utilities;
using Sandbox.Definitions;
using VRage.Game;

namespace Digi.BuildInfo.Features.Tooltips
{
    public class BlueprintTooltips : ModComponent
    {
        const int ListLimit = 6;

        Dictionary<string, Sizes> TmpNameAndSize = new Dictionary<string, Sizes>();

        void DisposeTempObjects()
        {
            TmpNameAndSize = null;
        }

        enum Sizes { Small, Large, Both, HandWeapon }

        StringBuilder SB = new StringBuilder(512);

        public BlueprintTooltips(BuildInfoMod main) : base(main)
        {
            Main.TooltipHandler.Setup += Setup;
        }

        public override void RegisterComponent()
        {
        }

        public override void UnregisterComponent()
        {
            if(!Main.ComponentsRegistered)
                return;

            Main.TooltipHandler.Setup -= Setup;
        }

        void Setup(bool generate)
        {
            foreach(var bpBaseDef in MyDefinitionManager.Static.GetBlueprintDefinitions())
            {
                HandleTooltip(bpBaseDef, generate);
            }

            if(generate)
            {
                DisposeTempObjects();
            }
        }

        void HandleTooltip(MyBlueprintDefinitionBase bpBaseDef, bool generate)
        {
            string tooltip = null;
            if(generate)
            {
                // generate tooltips and cache them alone
                SB.Clear();
                GenerateTooltip(SB, bpBaseDef);
                if(SB.Length > 5)
                {
                    tooltip = SB.ToString();
                    Main.TooltipHandler.Tooltips[bpBaseDef.Id] = tooltip;
                }
            }
            else
            {
                // retrieve cached tooltip string
                tooltip = Main.TooltipHandler.Tooltips.GetValueOrDefault(bpBaseDef.Id, null);
            }

            SB.Clear();
            SB.Append(bpBaseDef.DisplayNameText); // get existing text, then replace/append to it as needed

            if(tooltip != null)
            {
                // tooltip likely contains the cached tooltip, get rid of it.
                if(SB.Length >= tooltip.Length)
                {
                    SB.Replace(tooltip, "");
                }

                if(Main.Config.ItemTooltipAdditions.Value)
                {
                    SB.Append(tooltip);
                }
            }

            #region internal info
            const string BpIdLabel = "\nBlueprint Id: ";
            // TODO crafted items too?
            //const string CraftIdLabel = "\nCrafted Id: ";

            if(SB.Length > 0)
            {
                SB.RemoveLineStartsWith(BpIdLabel);
                //TempSB.RemoveLineStartsWith(CraftIdLabel);
            }

            if(Main.Config.InternalInfo.Value)
            {
                int obPrefixLen = "MyObjectBuilder_".Length;
                string typeIdString = bpBaseDef.Id.TypeId.ToString();
                SB.Append(BpIdLabel).Append(typeIdString, obPrefixLen, (typeIdString.Length - obPrefixLen)).Append("/").Append(bpBaseDef.Id.SubtypeName);

                //var resultDef = bpDef.Results...;

                //string typeIdString = bpDef.Id.TypeId.ToString();
                //TempSB.Append(BpIdLabel).Append(typeIdString, obPrefixLen, (typeIdString.Length - obPrefixLen)).Append("/").Append(bpDef.Id.SubtypeName);
            }
            #endregion internal info

            bpBaseDef.DisplayNameEnum = null; // prevent this from being used instead of DisplayNameString
            bpBaseDef.DisplayNameString = SB.ToString();
        }

        void GenerateTooltip(StringBuilder s, MyBlueprintDefinitionBase bpBaseDef)
        {
            s.Append('\n');

            if(!Main.TooltipHandler.IgnoreModItems.Contains(bpBaseDef.Id))
            {
                // TODO: append block/item description?

                if(bpBaseDef.Results != null && bpBaseDef.Results.Length > 0)
                {
                    TooltipWeaponBp(s, bpBaseDef);
                    TooltipAmmoBp(s, bpBaseDef);
                }
            }

            if(!bpBaseDef.Context.IsBaseGame)
            {
                s.Append("\nMod: ").AppendMaxLength(bpBaseDef.Context.ModName, TextGeneration.MOD_NAME_MAX_LENGTH);

                var workshopId = bpBaseDef.Context.GetWorkshopID();
                if(workshopId > 0)
                    s.Append(" (id: ").Append(workshopId).Append(")");
            }
        }

        void TooltipWeaponBp(StringBuilder s, MyBlueprintDefinitionBase bpBaseDef)
        {
            var weaponItemDef = MyDefinitionManager.Static.GetDefinition(bpBaseDef.Results[0].Id) as MyWeaponItemDefinition;
            if(weaponItemDef == null)
                return;

            MyWeaponDefinition weaponDef;
            if(!MyDefinitionManager.Static.TryGetWeaponDefinition(weaponItemDef.WeaponDefinitionId, out weaponDef))
                return;

            // TODO some weapon stats? they depend on the ammo tho...

            if(weaponDef.AmmoMagazinesId != null && weaponDef.AmmoMagazinesId.Length > 0)
            {
                if(weaponDef.AmmoMagazinesId.Length == 1)
                    s.Append("\nUses magazine:");
                else
                    s.Append("\nUses magazines:");

                foreach(var magId in weaponDef.AmmoMagazinesId)
                {
                    s.Append("\n  ");

                    var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                    if(magDef == null)
                        s.Append("(NotFound=").Append(magId.ToString()).Append(")");
                    else
                        s.Append(magDef.DisplayNameText);

                    if(!Main.TooltipHandler.TmpHasBP.Contains(magId))
                        s.Append(" (Not Craftable)");
                }
            }
        }

        void TooltipAmmoBp(StringBuilder s, MyBlueprintDefinitionBase bpBaseDef)
        {
            var magDef = MyDefinitionManager.Static.GetDefinition(bpBaseDef.Results[0].Id) as MyAmmoMagazineDefinition;
            if(magDef == null)
                return;

            if(magDef.Capacity > 1)
                s.Append("\nMagazine Capacity: ").Append(magDef.Capacity);

            TmpNameAndSize.Clear();

            foreach(var def in MyDefinitionManager.Static.GetAllDefinitions())
            {
                {
                    var weaponItemDef = def as MyWeaponItemDefinition;
                    if(weaponItemDef != null)
                    {
                        MyWeaponDefinition wpDef;
                        if(!MyDefinitionManager.Static.TryGetWeaponDefinition(weaponItemDef.WeaponDefinitionId, out wpDef))
                            continue;

                        if(wpDef.AmmoMagazinesId != null && wpDef.AmmoMagazinesId.Length > 0)
                        {
                            foreach(var magId in wpDef.AmmoMagazinesId)
                            {
                                if(magId == magDef.Id)
                                {
                                    TmpNameAndSize.Add(def.DisplayNameText, Sizes.HandWeapon);
                                    break;
                                }
                            }
                        }
                        continue;
                    }
                }
                {
                    var weaponBlockDef = def as MyWeaponBlockDefinition;
                    if(weaponBlockDef != null)
                    {
                        MyWeaponDefinition wpDef;
                        if(!MyDefinitionManager.Static.TryGetWeaponDefinition(weaponBlockDef.WeaponDefinitionId, out wpDef))
                            continue;

                        if(wpDef != null && wpDef.AmmoMagazinesId != null && wpDef.AmmoMagazinesId.Length > 0)
                        {
                            foreach(var magId in wpDef.AmmoMagazinesId)
                            {
                                if(magId == magDef.Id)
                                {
                                    string key = def.DisplayNameText;
                                    Sizes currentSize = (weaponBlockDef.CubeSize == MyCubeSize.Small ? Sizes.Small : Sizes.Large);
                                    Sizes existingSize;
                                    if(TmpNameAndSize.TryGetValue(key, out existingSize))
                                    {
                                        if(existingSize != Sizes.Both && existingSize != currentSize)
                                            TmpNameAndSize[key] = Sizes.Both;
                                    }
                                    else
                                    {
                                        TmpNameAndSize[key] = currentSize;
                                    }
                                    break;
                                }
                            }
                        }
                        continue;
                    }
                }
            }

            if(TmpNameAndSize.Count == 0)
                return;

            s.Append("\nUsed by:");

            int limit = 0;
            foreach(var kv in TmpNameAndSize)
            {
                if(++limit > ListLimit)
                {
                    limit--;
                    s.Append("\n  ...and ").Append(TmpNameAndSize.Count - limit).Append(" more");
                    break;
                }

                s.Append("\n  ").Append(kv.Key);

                switch(kv.Value)
                {
                    case Sizes.Small: s.Append(" (Small Grid)"); break;
                    case Sizes.Large: s.Append(" (Large Grid)"); break;
                    case Sizes.Both: s.Append(" (Small + Large Grid)"); break;
                    case Sizes.HandWeapon: s.Append(" (Hand-held)"); break;
                }
            }
        }
    }
}
