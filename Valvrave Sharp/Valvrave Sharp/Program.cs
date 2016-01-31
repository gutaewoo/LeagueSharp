﻿namespace Valvrave_Sharp
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Text.RegularExpressions;

    using LeagueSharp;
    using LeagueSharp.SDK;
    using LeagueSharp.SDK.Core.UI.IMenu;
    using LeagueSharp.SDK.Core.Utils;

    using Valvrave_Sharp.Core;
    using Valvrave_Sharp.Plugin;

    #endregion

    internal class Program
    {
        #region Constants

        internal const int FlashRange = 425, IgniteRange = 600, SmiteRange = 570;

        #endregion

        #region Static Fields

        public static Menu MainMenu;

        internal static Items.Item Bilgewater, BotRuinedKing, Youmuu, Tiamat, Hydra, Titanic;

        internal static SpellSlot Flash, Ignite, Smite;

        internal static Spell Q, Q2, W, W2, E, E2, R, R2;

        private static readonly Dictionary<string, Func<object>> Plugins = new Dictionary<string, Func<object>>
                                                                               {
                                                                                   { "Kennen", () => new Kennen() },
                                                                                   { "LeeSin", () => new LeeSin() },
                                                                                   { "Yasuo", () => new Yasuo() },
                                                                                   { "Zed", () => new Zed() }
                                                                               };

        #endregion

        #region Properties

        internal static Obj_AI_Hero Player => ObjectManager.Player;

        #endregion

        #region Methods

        private static void InitItem()
        {
            Bilgewater = new Items.Item(ItemId.Bilgewater_Cutlass, 550);
            BotRuinedKing = new Items.Item(ItemId.Blade_of_the_Ruined_King, 550);
            Youmuu = new Items.Item(ItemId.Youmuus_Ghostblade, 0);
            Tiamat = new Items.Item(ItemId.Tiamat_Melee_Only, 400);
            Hydra = new Items.Item(ItemId.Ravenous_Hydra_Melee_Only, 400);
            Titanic = new Items.Item(3748, 0);
        }

        private static void InitMenu(bool isSupport)
        {
            MainMenu = new Menu("ValvraveSharp", "Valvrave Sharp", true, Player.ChampionName).Attach();
            MainMenu.Separator("Author: Brian");
            MainMenu.Separator("Paypal: dcbrian01@gmail.com");
            if (isSupport)
            {
                Plugins[Player.ChampionName].Invoke();
                Invulnerable.Deregister(new InvulnerableEntry("FerociousHowl"));
                Invulnerable.Deregister(new InvulnerableEntry("Meditate"));
                /*Drawing.OnDraw +=
                    args => { Tracker.DetectedSkillshots.ForEach(i => i.Draw(Color.AliceBlue, Color.Red)); };*/
            }
            else
            {
                MainMenu.Separator(Player.ChampionName + " Not Support");
            }
        }

        private static void InitSummonerSpell()
        {
            var smiteName =
                Player.Spellbook.Spells.Where(
                    i =>
                    (i.Slot == SpellSlot.Summoner1 || i.Slot == SpellSlot.Summoner2)
                    && i.Name.ToLower().Contains("smite")).Select(i => i.Name).FirstOrDefault();
            if (!string.IsNullOrEmpty(smiteName))
            {
                Smite = Player.GetSpellSlot(smiteName);
            }
            Ignite = Player.GetSpellSlot("summonerdot");
            Flash = Player.GetSpellSlot("summonerflash");
        }

        private static void Main(string[] args)
        {
            if (args == null)
            {
                return;
            }
            Events.OnLoad += (sender, eventArgs) =>
                {
                    UpdateCheck();
                    var checkSupport = Plugins.ContainsKey(Player.ChampionName);
                    InitMenu(checkSupport);
                    if (!checkSupport)
                    {
                        return;
                    }
                    InitItem();
                    InitSummonerSpell();
                };
        }

        private static void PrintChat(string text)
        {
            Game.PrintChat("Valvrave Sharp => {0}", text);
        }

        private static void UpdateCheck()
        {
            try
            {
                using (var web = new WebClient())
                {
                    var rawFile =
                        web.DownloadString(
                            "https://raw.githubusercontent.com/brian0305/LeagueSharp/master/Valvrave%20Sharp/Valvrave%20Sharp/Properties/AssemblyInfo.cs");
                    var checkFile =
                        new Regex(@"\[assembly\: AssemblyVersion\(""(\d{1,})\.(\d{1,})\.(\d{1,})\.(\d{1,})""\)\]").Match
                            (rawFile);
                    if (!checkFile.Success)
                    {
                        return;
                    }
                    var gitVersion =
                        new Version(
                            $"{checkFile.Groups[1]}.{checkFile.Groups[2]}.{checkFile.Groups[3]}.{checkFile.Groups[4]}");
                    if (gitVersion > Assembly.GetExecutingAssembly().GetName().Version)
                    {
                        PrintChat("Outdated! Newest Version: " + gitVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        #endregion
    }
}