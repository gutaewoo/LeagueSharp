﻿namespace Valvrave_Sharp.Plugin
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Windows.Forms;

    using LeagueSharp;
    using LeagueSharp.SDK;
    using LeagueSharp.SDK.Core.UI.IMenu.Values;
    using LeagueSharp.SDK.Core.Utils;
    using LeagueSharp.SDK.Core.Wrappers.Damages;

    using Valvrave_Sharp.Core;

    using Menu = LeagueSharp.SDK.Core.UI.IMenu.Menu;

    #endregion

    internal class Kennen : Program
    {
        #region Static Fields

        private static readonly Items.Item Wooglet = new Items.Item(3090, 0);

        private static readonly Items.Item Zhonya = new Items.Item(3157, 0);

        #endregion

        #region Constructors and Destructors

        public Kennen()
        {
            Q = new Spell(SpellSlot.Q, 1050).SetSkillshot(0.2f, 50, 1700, true, SkillshotType.SkillshotLine);
            W = new Spell(SpellSlot.W, 950);
            E = new Spell(SpellSlot.E);
            R = new Spell(SpellSlot.R, 550);
            Q.DamageType = W.DamageType = R.DamageType = DamageType.Magical;
            Q.MinHitChance = HitChance.VeryHigh;

            var comboMenu = MainMenu.Add(new Menu("Combo", "Combo"));
            {
                comboMenu.Bool("Ignite", "Use Ignite");
                comboMenu.Bool("Q", "Use Q");
                comboMenu.Bool("W", "Use W");
                comboMenu.Separator("R Settings");
                comboMenu.Bool("R", "Use R");
                comboMenu.Slider("RHpU", "If Enemy Hp < (%)", 60);
                comboMenu.Slider("RCountA", "Or Enemy >=", 2, 1, 5);
                comboMenu.Separator("Zhonya Settings For R Combo");
                comboMenu.Bool("Zhonya", "Use Zhonya");
                comboMenu.Slider("ZhonyaHpU", "If Hp < (%)", 20);
            }
            var hybridMenu = MainMenu.Add(new Menu("Hybrid", "Hybrid"));
            {
                hybridMenu.Bool("Q", "Use Q");
                hybridMenu.Separator("W Settings");
                hybridMenu.Bool("W", "Use W");
                hybridMenu.Slider("WMpA", "If Mp >=", 100, 0, 200);
                hybridMenu.Separator("Auto Q Settings");
                hybridMenu.KeyBind("AutoQ", "KeyBind", Keys.T, KeyBindType.Toggle);
                hybridMenu.Slider("AutoQMpA", "If Mp >=", 100, 0, 200);
            }
            var lhMenu = MainMenu.Add(new Menu("LastHit", "Last Hit"));
            {
                lhMenu.Bool("Q", "Use Q");
            }
            var ksMenu = MainMenu.Add(new Menu("KillSteal", "Kill Steal"));
            {
                ksMenu.Bool("Q", "Use Q");
                ksMenu.Bool("W", "Use W");
            }
            var drawMenu = MainMenu.Add(new Menu("Draw", "Draw"));
            {
                drawMenu.Bool("Q", "Q Range", false);
                drawMenu.Bool("W", "W Range", false);
                drawMenu.Bool("R", "R Range", false);
            }
            MainMenu.KeyBind("FleeE", "Use E To Flee", Keys.C);

            Game.OnUpdate += OnUpdate;
            Drawing.OnDraw += OnDraw;
        }

        #endregion

        #region Properties

        private static List<Obj_AI_Hero> GetWTarget
            => Variables.TargetSelector.GetTargets(W.Range, W.DamageType).Where(i => HaveW(i)).ToList();

        private static bool HaveE => Player.HasBuff("KennenLightningRush");

        private static bool HaveR => Player.HasBuff("KennenShurikenStorm");

        #endregion

        #region Methods

        private static void AutoQ()
        {
            if (!Q.IsReady() || !MainMenu["Hybrid"]["AutoQ"].GetValue<MenuKeyBind>().Active
                || Player.Mana < MainMenu["Hybrid"]["AutoQMpA"])
            {
                return;
            }
            var target = Q.GetTarget(Q.Width / 2);
            if (target == null)
            {
                return;
            }
            var pred = Q.VPrediction(target);
            if (pred.Hitchance >= Q.MinHitChance)
            {
                Q.Cast(pred.CastPosition);
            }
        }

        private static void Combo()
        {
            if (MainMenu["Combo"]["R"])
            {
                if (R.IsReady())
                {
                    var target = Variables.TargetSelector.GetTargets(R.Range, R.DamageType, false);
                    if (((target.Count > 1 && target.Any(i => i.Health + i.MagicalShield <= R.GetDamage(i)))
                         || target.Sum(i => i.HealthPercent) / target.Count <= MainMenu["Combo"]["RHpU"]
                         || target.Count >= MainMenu["Combo"]["RCountA"]) && R.Cast())
                    {
                        return;
                    }
                }
                else if (HaveR && MainMenu["Combo"]["Zhonya"] && Player.HealthPercent < MainMenu["Combo"]["ZhonyaHpU"]
                         && Player.CountEnemyHeroesInRange(W.Range) > 0)
                {
                    if (Zhonya.IsReady)
                    {
                        Zhonya.Cast();
                    }
                    if (Wooglet.IsReady)
                    {
                        Wooglet.Cast();
                    }
                }
            }
            if (MainMenu["Combo"]["Q"] && Q.CastingBestTarget(Q.Width / 2) == CastStates.SuccessfullyCasted)
            {
                return;
            }
            if (MainMenu["Combo"]["W"] && W.IsReady() && GetWTarget.Count > 0)
            {
                if (HaveR)
                {
                    var target = GetWTarget;
                    if ((target.Count(i => HaveW(i, true)) > 1
                         || target.Any(i => i.Health + i.MagicalShield <= W.GetDamage(i, Damage.DamageStage.Empowered))
                         || target.Count > 2 || (target.Count(i => HaveW(i, true)) > 0 && target.Count > 1)) && W.Cast())
                    {
                        return;
                    }
                }
                else if (W.Cast())
                {
                    return;
                }
            }
            var subTarget = W.GetTarget();
            if (subTarget != null && MainMenu["Combo"]["Ignite"] && Ignite.IsReady() && subTarget.HealthPercent < 30
                && subTarget.DistanceToPlayer() <= IgniteRange)
            {
                Player.Spellbook.CastSpell(Ignite, subTarget);
            }
        }

        private static bool HaveW(Obj_AI_Base target, bool checkCanStun = false)
        {
            var buff = target.GetBuffCount("KennenMarkOfStorm");
            return buff != -1 && (!checkCanStun || buff == 2);
        }

        private static void Hybrid()
        {
            if (MainMenu["Hybrid"]["Q"] && Q.CastingBestTarget(Q.Width / 2) == CastStates.SuccessfullyCasted)
            {
                return;
            }
            if (MainMenu["Hybrid"]["W"] && W.IsReady() && Player.Mana >= MainMenu["Hybrid"]["WMpA"]
                && GetWTarget.Count > 0)
            {
                W.Cast();
            }
        }

        private static void KillSteal()
        {
            if (MainMenu["KillSteal"]["Q"] && Q.IsReady())
            {
                var target = Q.GetTarget(Q.Width / 2);
                if (target != null)
                {
                    if ((target.Health + target.MagicalShield <= Q.GetDamage(target))
                        || (MainMenu["KillSteal"]["W"] && W.IsInRange(target)
                            && W.Instance.State == SpellState.Surpressed
                            && target.Health + target.MagicalShield
                            <= Q.GetDamage(target) + W.GetDamage(target, Damage.DamageStage.Empowered)))
                    {
                        var pred = Q.VPrediction(
                            target,
                            false,
                            CollisionableObjects.Heroes | CollisionableObjects.Minions | CollisionableObjects.YasuoWall);
                        if (pred.Hitchance >= Q.MinHitChance && Q.Cast(pred.CastPosition))
                        {
                            return;
                        }
                    }
                }
            }
            if (MainMenu["KillSteal"]["W"] && W.IsReady()
                && GetWTarget.Any(i => i.Health + i.MagicalShield <= W.GetDamage(i, Damage.DamageStage.Empowered)))
            {
                W.Cast();
            }
        }

        private static void LastHit()
        {
            if (!MainMenu["LastHit"]["Q"] || !Q.IsReady())
            {
                return;
            }
            foreach (var pred in
                GameObjects.EnemyMinions.Where(
                    i =>
                    i.IsValidTarget(Q.Range) && (i.IsMinion() || i.IsPet(false))
                    && Q.GetHealthPrediction(i) <= Q.GetDamage(i)
                    && (!i.InAutoAttackRange() ? Q.GetHealthPrediction(i) > 0 : i.Health > Player.GetAutoAttackDamage(i)))
                    .OrderByDescending(i => i.MaxHealth)
                    .Select(
                        i =>
                        Q.VPrediction(
                            i,
                            false,
                            CollisionableObjects.Heroes | CollisionableObjects.Minions | CollisionableObjects.YasuoWall))
                    .Where(i => i.Hitchance >= Q.MinHitChance))
            {
                Q.Cast(pred.CastPosition);
            }
        }

        private static void OnDraw(EventArgs args)
        {
            if (Player.IsDead)
            {
                return;
            }
            if (MainMenu["Draw"]["Q"] && Q.Level > 0)
            {
                Drawing.DrawCircle(Player.Position, Q.Range, Q.IsReady() ? Color.LimeGreen : Color.IndianRed);
            }
            if (MainMenu["Draw"]["W"] && W.Level > 0)
            {
                Drawing.DrawCircle(Player.Position, W.Range, W.IsReady() ? Color.LimeGreen : Color.IndianRed);
            }
            if (MainMenu["Draw"]["R"] && R.Level > 0)
            {
                Drawing.DrawCircle(Player.Position, R.Range, R.IsReady() ? Color.LimeGreen : Color.IndianRed);
            }
        }

        private static void OnUpdate(EventArgs args)
        {
            if (Player.IsDead || MenuGUI.IsChatOpen || MenuGUI.IsShopOpen || Player.IsRecalling())
            {
                return;
            }
            KillSteal();
            Variables.Orbwalker.SetAttackState(!HaveE);
            switch (Variables.Orbwalker.GetActiveMode())
            {
                case OrbwalkingMode.Combo:
                    Combo();
                    break;
                case OrbwalkingMode.Hybrid:
                    Hybrid();
                    break;
                case OrbwalkingMode.LastHit:
                    LastHit();
                    break;
                case OrbwalkingMode.None:
                    if (MainMenu["FleeE"].GetValue<MenuKeyBind>().Active)
                    {
                        Variables.Orbwalker.Move(Game.CursorPos);
                        if (E.IsReady() && !HaveE)
                        {
                            E.Cast();
                        }
                    }
                    break;
            }
            if (Variables.Orbwalker.GetActiveMode() < OrbwalkingMode.Combo
                || Variables.Orbwalker.GetActiveMode() > OrbwalkingMode.Hybrid)
            {
                AutoQ();
            }
        }

        #endregion
    }
}