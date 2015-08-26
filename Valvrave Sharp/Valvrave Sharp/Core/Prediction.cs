﻿namespace Valvrave_Sharp.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    using LeagueSharp;
    using LeagueSharp.SDK.Core;
    using LeagueSharp.SDK.Core.Enumerations;
    using LeagueSharp.SDK.Core.Events;
    using LeagueSharp.SDK.Core.Extensions;
    using LeagueSharp.SDK.Core.Extensions.SharpDX;
    using LeagueSharp.SDK.Core.Math;
    using LeagueSharp.SDK.Core.Math.Prediction;
    using LeagueSharp.SDK.Core.Utils;

    using SharpDX;

    internal class Prediction
    {
        #region Public Methods and Operators

        public static PredictionOutput GetPrediction(Obj_AI_Base unit, float delay)
        {
            return GetPrediction(new PredictionInput { Unit = unit, Delay = delay });
        }

        public static PredictionOutput GetPrediction(Obj_AI_Base unit, float delay, float radius)
        {
            return GetPrediction(new PredictionInput { Unit = unit, Delay = delay, Radius = radius });
        }

        public static PredictionOutput GetPrediction(Obj_AI_Base unit, float delay, float radius, float speed)
        {
            return GetPrediction(new PredictionInput { Unit = unit, Delay = delay, Radius = radius, Speed = speed });
        }

        public static PredictionOutput GetPrediction(PredictionInput input)
        {
            return GetPrediction(input, true, true);
        }

        #endregion

        #region Methods

        private static double GetAngle(Vector3 from, Obj_AI_Base target)
        {
            var c = target.ServerPosition.ToVector2();
            var a = target.GetWaypoints().Last();
            if (c == a)
            {
                return 60;
            }
            var b = from.ToVector2();
            var ab = Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
            var bc = Math.Pow(b.X - c.X, 2) + Math.Pow(b.Y - c.Y, 2);
            var ac = Math.Pow(a.X - c.X, 2) + Math.Pow(a.Y - c.Y, 2);
            return Math.Cos((ab + bc - ac) / (2 * Math.Sqrt(ab) * Math.Sqrt(bc))) * 180 / Math.PI;
        }

        private static PredictionOutput GetDashingPrediction(PredictionInput input)
        {
            var dashData = input.Unit.GetDashInfo();
            var result = new PredictionOutput { Input = input };
            input.Delay += 0.1f;
            if (!dashData.IsBlink)
            {
                var dashPred = GetPositionOnPath(
                    input,
                    new List<Vector2> { input.Unit.ServerPosition.ToVector2(), dashData.Path.Last() },
                    dashData.Speed);
                if (dashPred.Hitchance >= HitChance.High)
                {
                    dashPred.CastPosition = dashPred.UnitPosition;
                    dashPred.Hitchance = HitChance.Dashing;
                    return dashPred;
                }
                if (dashData.Path.PathLength() > 200)
                {
                    var endP = dashData.Path.Last();
                    var timeToPoint = input.Delay + input.From.ToVector2().Distance(endP) / input.Speed;
                    if (timeToPoint
                        <= input.Unit.Distance(endP) / dashData.Speed + input.RealRadius / input.Unit.MoveSpeed)
                    {
                        return new PredictionOutput
                                   {
                                       CastPosition = endP.ToVector3(), UnitPosition = endP.ToVector3(),
                                       Hitchance = HitChance.Dashing
                                   };
                    }
                }
                result.CastPosition = dashData.Path.Last().ToVector3();
                result.UnitPosition = result.CastPosition;
            }
            return result;
        }

        private static PredictionOutput GetImmobilePrediction(PredictionInput input, double remainingImmobileT)
        {
            var timeToReachTargetPosition = input.Delay + input.Unit.Distance(input.From) / input.Speed;
            return timeToReachTargetPosition <= remainingImmobileT + input.RealRadius / input.Unit.MoveSpeed
                       ? new PredictionOutput
                             {
                                 CastPosition = input.Unit.ServerPosition, UnitPosition = input.Unit.Position,
                                 Hitchance = HitChance.Immobile
                             }
                       : new PredictionOutput
                             {
                                 Input = input, CastPosition = input.Unit.ServerPosition,
                                 UnitPosition = input.Unit.ServerPosition, Hitchance = HitChance.High
                             };
        }

        private static PredictionOutput GetPositionOnPath(PredictionInput input, List<Vector2> path, float speed = -1)
        {
            speed = Math.Abs(speed - (-1)) < float.Epsilon ? input.Unit.MoveSpeed : speed;
            if (path.Count <= 1)
            {
                return new PredictionOutput
                           {
                               Input = input, UnitPosition = input.Unit.ServerPosition,
                               CastPosition = input.Unit.ServerPosition, Hitchance = HitChance.VeryHigh
                           };
            }
            var pLength = path.PathLength();
            if (pLength >= input.Delay * speed - input.RealRadius
                && Math.Abs(input.Speed - float.MaxValue) < float.Epsilon)
            {
                var tDistance = input.Delay * speed - input.RealRadius;
                for (var i = 0; i < path.Count - 1; i++)
                {
                    var a = path[i];
                    var b = path[i + 1];
                    var d = a.Distance(b);
                    if (d >= tDistance)
                    {
                        var direction = (b - a).Normalized();
                        var cp = a + direction * tDistance;
                        var p = a
                                + direction
                                * (i == path.Count - 2
                                       ? Math.Min(tDistance + input.RealRadius, d)
                                       : tDistance + input.RealRadius);
                        return new PredictionOutput
                                   {
                                       Input = input, CastPosition = cp.ToVector3(), UnitPosition = p.ToVector3(),
                                       Hitchance =
                                           Path.PathTracker.GetCurrentPath(input.Unit).Time < 0.1d
                                               ? HitChance.VeryHigh
                                               : HitChance.High
                                   };
                    }
                    tDistance -= d;
                }
            }
            if (pLength >= input.Delay * speed - input.RealRadius
                && Math.Abs(input.Speed - float.MaxValue) > float.Epsilon)
            {
                path =
                    path.CutPath(
                        input.Delay * speed
                        - ((input.Type == SkillshotType.SkillshotLine || input.Type == SkillshotType.SkillshotCone)
                           && input.Unit.DistanceSquared(input.From) < 200 * 200
                               ? 0
                               : input.RealRadius));
                var tT = 0f;
                for (var i = 0; i < path.Count - 1; i++)
                {
                    var a = path[i];
                    var b = path[i + 1];
                    var tB = a.Distance(b) / speed;
                    var direction = (b - a).Normalized();
                    a = a - speed * tT * direction;
                    var sol = a.VectorMovementCollision(b, speed, input.From.ToVector2(), input.Speed, tT);
                    var t = (float)sol[0];
                    var pos = (Vector2)sol[1];
                    if (pos.IsValid() && t >= tT && t <= tT + tB)
                    {
                        if (pos.DistanceSquared(b) < 20)
                        {
                            break;
                        }
                        var p = pos + input.RealRadius * direction;
                        /*if (input.Type == SkillshotType.SkillshotLine)
                        {
                            var alpha = (input.From.ToVector2() - p).AngleBetween(a - b);
                            if (alpha > 30 && alpha < 180 - 30)
                            {
                                var beta = (float)Math.Asin(input.RealRadius / p.Distance(input.From));
                                var cp1 = input.From.ToVector2() + (p - input.From.ToVector2()).Rotated(beta);
                                var cp2 = input.From.ToVector2() + (p - input.From.ToVector2()).Rotated(-beta);
                                pos = cp1.DistanceSquared(pos) < cp2.DistanceSquared(pos) ? cp1 : cp2;
                            }
                        }*/
                        return new PredictionOutput
                                   {
                                       Input = input, CastPosition = pos.ToVector3(), UnitPosition = p.ToVector3(),
                                       Hitchance =
                                           Path.PathTracker.GetCurrentPath(input.Unit).Time < 0.1d
                                               ? HitChance.VeryHigh
                                               : HitChance.High
                                   };
                    }
                    tT += tB;
                }
            }
            var position = path.Last();
            return new PredictionOutput
                       {
                           Input = input, CastPosition = position.ToVector3(), UnitPosition = position.ToVector3(),
                           Hitchance = HitChance.Medium
                       };
        }

        private static PredictionOutput GetPrediction(PredictionInput input, bool ft, bool checkCollision)
        {
            PredictionOutput result = null;
            if (!input.Unit.IsValidTarget(float.MaxValue, false))
            {
                return new PredictionOutput();
            }
            if (ft)
            {
                input.Delay += Game.Ping / 2000f + 0.06f;
                if (input.AoE)
                {
                    return Cluster.GetAoEPrediction(input);
                }
            }
            if (Math.Abs(input.Range - float.MaxValue) > float.Epsilon
                && input.Unit.DistanceSquared(input.RangeCheckFrom) > Math.Pow(input.Range * 1.5, 2))
            {
                return new PredictionOutput { Input = input };
            }
            if (input.Unit.IsDashing())
            {
                result = GetDashingPrediction(input);
            }
            else
            {
                var remainingImmobileT = UnitIsImmobileUntil(input.Unit);
                if (remainingImmobileT >= 0d)
                {
                    result = GetImmobilePrediction(input, remainingImmobileT);
                }
            }
            if (result == null)
            {
                result = GetStandardPrediction(input);
            }
            if (Math.Abs(input.Range - float.MaxValue) > float.Epsilon)
            {
                if (result.Hitchance >= HitChance.High
                    && input.RangeCheckFrom.DistanceSquared(input.Unit.Position)
                    > Math.Pow(input.Range + input.RealRadius * 3 / 4, 2))
                {
                    result.Hitchance = HitChance.Medium;
                }
                if (input.RangeCheckFrom.DistanceSquared(result.UnitPosition)
                    > Math.Pow(input.Range + (input.Type == SkillshotType.SkillshotCircle ? input.RealRadius : 0), 2))
                {
                    result.Hitchance = HitChance.OutOfRange;
                }
                if (input.RangeCheckFrom.DistanceSquared(result.CastPosition) > Math.Pow(input.Range, 2))
                {
                    if (result.Hitchance != HitChance.OutOfRange)
                    {
                        result.CastPosition = input.RangeCheckFrom
                                              + input.Range
                                              * (result.UnitPosition - input.RangeCheckFrom).Normalized().SetZ();
                    }
                    else
                    {
                        result.Hitchance = HitChance.OutOfRange;
                    }
                }
            }
            if (result.Hitchance > HitChance.Medium)
            {
                WayPointAnalysis(result, input);
            }
            if (checkCollision && input.Collision)
            {
                var positions = new List<Vector3> { result.UnitPosition, result.CastPosition, input.Unit.Position };
                var originalUnit = input.Unit;
                result.CollisionObjects = Collisions.GetCollision(positions, input);
                result.CollisionObjects.RemoveAll(i => i.NetworkId == originalUnit.NetworkId);
                result.Hitchance = result.CollisionObjects.Count > 0 ? HitChance.Collision : result.Hitchance;
            }
            return result;
        }

        private static PredictionOutput GetStandardPrediction(PredictionInput input)
        {
            var speed = input.Unit.MoveSpeed;
            if (input.Unit.DistanceSquared(input.From) < 200 * 200)
            {
                speed /= 1.5f;
            }
            var result = GetPositionOnPath(input, input.Unit.GetWaypoints(), speed);
            if (result.Hitchance >= HitChance.High && input.Unit is Obj_AI_Hero)
            {
            }
            return result;
        }

        private static double UnitIsImmobileUntil(Obj_AI_Base unit)
        {
            var result =
                unit.Buffs.Where(
                    i =>
                    i.IsActive && Game.Time <= i.EndTime
                    && (i.Type == BuffType.Charm || i.Type == BuffType.Knockup || i.Type == BuffType.Stun
                        || i.Type == BuffType.Suppression || i.Type == BuffType.Snare))
                    .Aggregate(0d, (current, buff) => Math.Max(current, buff.EndTime));
            return result - Game.Time;
        }

        private static void WayPointAnalysis(PredictionOutput result, PredictionInput input)
        {
            var totalDelay = input.Delay
                             + (Math.Abs(input.Speed - float.MaxValue) < float.Epsilon
                                    ? 0
                                    : input.Unit.Distance(input.From) / input.Speed);
            var fixRange = (input.Unit.MoveSpeed * totalDelay) / 2;
            var lastWaypiont = input.Unit.GetWaypoints().Last().ToVector3();
            if (input.Type == SkillshotType.SkillshotCircle)
            {
                fixRange -= input.Radius / 2;
            }
            switch (input.Type)
            {
                case SkillshotType.SkillshotLine:
                    if (input.Unit.Path.Count() > 0)
                    {
                        result.Hitchance = GetAngle(input.From, input.Unit) < 36 ? HitChance.VeryHigh : HitChance.High;
                    }
                    break;
                case SkillshotType.SkillshotCircle:
                    if (totalDelay < 1.1)
                    {
                        if (totalDelay < 0.7 && OnProcessSpellDetection.GetLastAutoAttackTime(input.Unit) < 0.1d)
                        {
                            result.Hitchance = HitChance.VeryHigh;
                        }
                        if (Path.PathTracker.GetCurrentPath(input.Unit).Time < 0.1d)
                        {
                            result.Hitchance = HitChance.VeryHigh;
                        }
                    }
                    break;
            }
            if (input.Unit.HasBuffOfType(BuffType.Slow) || input.Unit.Distance(input.From) < 300
                || lastWaypiont.Distance(input.From) < 250)
            {
                result.Hitchance = HitChance.VeryHigh;
            }
            if (input.Unit.Distance(lastWaypiont) > 800)
            {
                result.Hitchance = HitChance.VeryHigh;
            }
            if (input.Unit.Path.Count() == 0 && input.Unit.Position == input.Unit.ServerPosition
                && !input.Unit.IsWindingUp)
            {
                result.Hitchance = input.Unit.Distance(input.From) > input.Range - fixRange
                                       ? HitChance.High
                                       : HitChance.VeryHigh;
                return;
            }
            if (lastWaypiont.Distance(input.From) <= input.Unit.Distance(input.From)
                && input.Unit.Distance(input.From) > input.Range - fixRange)
            {
                result.Hitchance = HitChance.High;
            }
            var backToFront = input.Unit.MoveSpeed * totalDelay;
            if (input.Unit.Path.Count() > 0 && input.Unit.Distance(lastWaypiont) < backToFront)
            {
                result.Hitchance = HitChance.Medium;
            }
            if (totalDelay > 0.7
                && (input.Unit.IsWindingUp || OnProcessSpellDetection.GetLastAutoAttackTime(input.Unit) < 0.1d))
            {
                result.Hitchance = HitChance.Medium;
            }
            if (input.Unit.Path.Count() > 1 && input.Type == SkillshotType.SkillshotLine)
            {
                result.Hitchance = HitChance.Medium;
            }
            if (input.Unit.Distance(input.From) < 300 || lastWaypiont.Distance(input.From) < 250)
            {
                result.Hitchance = HitChance.VeryHigh;
            }
        }

        #endregion

        private static class Cluster
        {
            #region Methods

            internal static PredictionOutput GetAoEPrediction(PredictionInput input)
            {
                switch (input.Type)
                {
                    case SkillshotType.SkillshotCircle:
                        return Circle.GetCirclePrediction(input);
                    case SkillshotType.SkillshotCone:
                        return Cone.GetConePrediction(input);
                    case SkillshotType.SkillshotLine:
                        return Line.GetLinePrediction(input);
                }
                return new PredictionOutput();
            }

            private static List<PossibleTarget> GetPossibleTargets(PredictionInput input)
            {
                var result = new List<PossibleTarget>();
                var originalUnit = input.Unit;
                foreach (var enemy in
                    GameObjects.EnemyHeroes.Where(
                        i =>
                        i.NetworkId != originalUnit.NetworkId
                        && i.IsValidTarget(input.Range + 200 + input.RealRadius, true, input.RangeCheckFrom)))
                {
                    input.Unit = enemy;
                    var prediction = GetPrediction(input, false, false);
                    if (prediction.Hitchance >= HitChance.High)
                    {
                        result.Add(new PossibleTarget { Position = prediction.UnitPosition.ToVector2(), Unit = enemy });
                    }
                }
                return result;
            }

            #endregion

            private static class Circle
            {
                #region Methods

                internal static PredictionOutput GetCirclePrediction(PredictionInput input)
                {
                    var mainTargetPrediction = GetPrediction(input, false, true);
                    var posibleTargets = new List<PossibleTarget>
                                             {
                                                 new PossibleTarget
                                                     {
                                                         Position = mainTargetPrediction.UnitPosition.ToVector2(),
                                                         Unit = input.Unit
                                                     }
                                             };
                    if (mainTargetPrediction.Hitchance >= HitChance.Medium)
                    {
                        posibleTargets.AddRange(GetPossibleTargets(input));
                    }
                    while (posibleTargets.Count > 1)
                    {
                        var mecCircle = ConvexHull.GetMec(posibleTargets.Select(i => i.Position).ToList());
                        if (mecCircle.Radius <= input.RealRadius - 10
                            && mecCircle.Center.DistanceSquared(input.RangeCheckFrom.ToVector2())
                            < input.Range * input.Range)
                        {
                            return new PredictionOutput
                                       {
                                           AoeTargetsHit = posibleTargets.Select(i => (Obj_AI_Hero)i.Unit).ToList(),
                                           CastPosition = mecCircle.Center.ToVector3(),
                                           UnitPosition = mainTargetPrediction.UnitPosition,
                                           Hitchance = mainTargetPrediction.Hitchance, Input = input,
                                           AoeHitCount = posibleTargets.Count
                                       };
                        }
                        float maxdist = -1;
                        var maxdistindex = 1;
                        for (var i = 1; i < posibleTargets.Count; i++)
                        {
                            var distance = posibleTargets[i].Position.DistanceSquared(posibleTargets[0].Position);
                            if (distance > maxdist || maxdist.CompareTo(-1) == 0)
                            {
                                maxdistindex = i;
                                maxdist = distance;
                            }
                        }
                        posibleTargets.RemoveAt(maxdistindex);
                    }
                    return mainTargetPrediction;
                }

                #endregion
            }

            private static class Cone
            {
                #region Methods

                internal static PredictionOutput GetConePrediction(PredictionInput input)
                {
                    var mainTargetPrediction = GetPrediction(input, false, true);
                    var posibleTargets = new List<PossibleTarget>
                                             {
                                                 new PossibleTarget
                                                     {
                                                         Position = mainTargetPrediction.UnitPosition.ToVector2(),
                                                         Unit = input.Unit
                                                     }
                                             };
                    if (mainTargetPrediction.Hitchance >= HitChance.Medium)
                    {
                        posibleTargets.AddRange(GetPossibleTargets(input));
                    }
                    if (posibleTargets.Count > 1)
                    {
                        var candidates = new List<Vector2>();
                        foreach (var target in posibleTargets)
                        {
                            target.Position = target.Position - input.From.ToVector2();
                        }
                        for (var i = 0; i < posibleTargets.Count; i++)
                        {
                            for (var j = 0; j < posibleTargets.Count; j++)
                            {
                                if (i != j)
                                {
                                    var p = (posibleTargets[i].Position + posibleTargets[j].Position) * 0.5f;
                                    if (!candidates.Contains(p))
                                    {
                                        candidates.Add(p);
                                    }
                                }
                            }
                        }
                        var bestCandidateHits = -1;
                        var bestCandidate = new Vector2();
                        var positionsList = posibleTargets.Select(i => i.Position).ToList();
                        foreach (var candidate in candidates)
                        {
                            var hits = GetHits(candidate, input.Range, input.Radius, positionsList);
                            if (hits > bestCandidateHits)
                            {
                                bestCandidate = candidate;
                                bestCandidateHits = hits;
                            }
                        }
                        if (bestCandidateHits > 1 && input.From.ToVector2().DistanceSquared(bestCandidate) > 50 * 50)
                        {
                            return new PredictionOutput
                                       {
                                           Hitchance = mainTargetPrediction.Hitchance, AoeHitCount = bestCandidateHits,
                                           UnitPosition = mainTargetPrediction.UnitPosition,
                                           CastPosition = bestCandidate.ToVector3(), Input = input
                                       };
                        }
                    }
                    return mainTargetPrediction;
                }

                private static int GetHits(Vector2 end, double range, float angle, List<Vector2> points)
                {
                    return (from point in points
                            let edge1 = end.Rotated(-angle / 2)
                            let edge2 = edge1.Rotated(angle)
                            where
                                point.DistanceSquared(new Vector2()) < range * range && edge1.CrossProduct(point) > 0
                                && point.CrossProduct(edge2) > 0
                            select point).Count();
                }

                #endregion
            }

            private static class Line
            {
                #region Methods

                internal static PredictionOutput GetLinePrediction(PredictionInput input)
                {
                    var mainTargetPrediction = GetPrediction(input, false, true);
                    var posibleTargets = new List<PossibleTarget>
                                             {
                                                 new PossibleTarget
                                                     {
                                                         Position = mainTargetPrediction.UnitPosition.ToVector2(),
                                                         Unit = input.Unit
                                                     }
                                             };
                    if (mainTargetPrediction.Hitchance >= HitChance.Medium)
                    {
                        posibleTargets.AddRange(GetPossibleTargets(input));
                    }
                    if (posibleTargets.Count > 1)
                    {
                        var candidates = new List<Vector2>();
                        foreach (var targetCandidates in
                            posibleTargets.Select(
                                i => GetCandidates(input.From.ToVector2(), i.Position, input.Radius, input.Range)))
                        {
                            candidates.AddRange(targetCandidates);
                        }
                        var bestCandidateHits = -1;
                        var bestCandidate = new Vector2();
                        var bestCandidateHitPoints = new List<Vector2>();
                        var positionsList = posibleTargets.Select(i => i.Position).ToList();
                        foreach (var candidate in candidates)
                        {
                            if (
                                GetHits(
                                    input.From.ToVector2(),
                                    candidate,
                                    input.Radius + input.Unit.BoundingRadius / 3 - 10,
                                    new List<Vector2> { posibleTargets[0].Position }).Count == 1)
                            {
                                var hits = GetHits(input.From.ToVector2(), candidate, input.Radius, positionsList);
                                var hitsCount = hits.Count;
                                if (hitsCount >= bestCandidateHits)
                                {
                                    bestCandidateHits = hitsCount;
                                    bestCandidate = candidate;
                                    bestCandidateHitPoints = hits;
                                }
                            }
                        }
                        if (bestCandidateHits > 1)
                        {
                            float maxDistance = -1;
                            Vector2 p1 = new Vector2(), p2 = new Vector2();
                            for (var i = 0; i < bestCandidateHitPoints.Count; i++)
                            {
                                for (var j = 0; j < bestCandidateHitPoints.Count; j++)
                                {
                                    var startP = input.From.ToVector2();
                                    var endP = bestCandidate;
                                    var proj1 = positionsList[i].ProjectOn(startP, endP);
                                    var proj2 = positionsList[j].ProjectOn(startP, endP);
                                    var dist = bestCandidateHitPoints[i].DistanceSquared(proj1.LinePoint)
                                               + bestCandidateHitPoints[j].DistanceSquared(proj2.LinePoint);
                                    if (dist >= maxDistance
                                        && (proj1.LinePoint - positionsList[i]).AngleBetween(
                                            proj2.LinePoint - positionsList[j]) > 90)
                                    {
                                        maxDistance = dist;
                                        p1 = positionsList[i];
                                        p2 = positionsList[j];
                                    }
                                }
                            }
                            return new PredictionOutput
                                       {
                                           Hitchance = mainTargetPrediction.Hitchance, AoeHitCount = bestCandidateHits,
                                           UnitPosition = mainTargetPrediction.UnitPosition,
                                           CastPosition = ((p1 + p2) * 0.5f).ToVector3(), Input = input
                                       };
                        }
                    }
                    return mainTargetPrediction;
                }

                private static Vector2[] GetCandidates(Vector2 from, Vector2 to, float radius, float range)
                {
                    var middlePoint = (from + to) / 2;
                    var intersections = @from.CircleCircleIntersection(middlePoint, radius, from.Distance(middlePoint));
                    if (intersections.Length <= 1)
                    {
                        return new Vector2[] { };
                    }
                    var c1 = intersections[0];
                    var c2 = intersections[1];
                    c1 = @from + range * (to - c1).Normalized();
                    c2 = @from + range * (to - c2).Normalized();
                    return new[] { c1, c2 };
                }

                private static List<Vector2> GetHits(Vector2 start, Vector2 end, double radius, List<Vector2> points)
                {
                    return points.Where(i => i.DistanceSquared(start, end, true) <= radius * radius).ToList();
                }

                #endregion
            }

            private class PossibleTarget
            {
                #region Properties

                internal Vector2 Position { get; set; }

                internal Obj_AI_Base Unit { get; set; }

                #endregion
            }
        }

        private static class Collisions
        {
            #region Static Fields

            private static int wallCastT;

            private static Vector2 yasuoWallCastedPos;

            #endregion

            #region Constructors and Destructors

            static Collisions()
            {
                Obj_AI_Base.OnProcessSpellCast += OnProcessSpellCast;
            }

            #endregion

            #region Methods

            internal static List<Obj_AI_Base> GetCollision(List<Vector3> positions, PredictionInput input)
            {
                var result = new List<Obj_AI_Base>();
                foreach (var position in positions)
                {
                    foreach (var objType in input.CollisionObjects)
                    {
                        switch (objType)
                        {
                            case CollisionableObjects.Minions:
                                foreach (var minion in
                                    GameObjects.EnemyMinions.Where(
                                        i =>
                                        i.IsValidTarget(
                                            Math.Min(input.Range + input.Radius + 100, 2000),
                                            true,
                                            input.RangeCheckFrom)))
                                {
                                    input.Unit = minion;
                                    var pred = GetPrediction(input, false, false);
                                    if (pred.UnitPosition.ToVector2()
                                            .DistanceSquared(input.From.ToVector2(), position.ToVector2(), true)
                                        <= Math.Pow(input.Radius + 15 + minion.BoundingRadius, 2))
                                    {
                                        result.Add(minion);
                                    }
                                }
                                break;
                            case CollisionableObjects.Heroes:
                                foreach (var hero in
                                    GameObjects.EnemyHeroes.Where(
                                        i =>
                                        i.IsValidTarget(
                                            Math.Min(input.Range + input.Radius + 100, 2000),
                                            true,
                                            input.RangeCheckFrom)))
                                {
                                    input.Unit = hero;
                                    var pred = GetPrediction(input, false, false);
                                    if (pred.UnitPosition.ToVector2()
                                            .DistanceSquared(input.From.ToVector2(), position.ToVector2(), true)
                                        <= Math.Pow(input.Radius + 50 + hero.BoundingRadius, 2))
                                    {
                                        result.Add(hero);
                                    }
                                }
                                break;
                            case CollisionableObjects.Walls:
                                var step = position.Distance(input.From) / 20;
                                for (var i = 0; i < 20; i++)
                                {
                                    var p = input.From.ToVector2().Extend(position.ToVector2(), step * i);
                                    if (NavMesh.GetCollisionFlags(p.X, p.Y).HasFlag(CollisionFlags.Wall))
                                    {
                                        result.Add(ObjectManager.Player);
                                    }
                                }
                                break;
                            case CollisionableObjects.YasuoWall:
                                if (Variables.TickCount - wallCastT > 4000)
                                {
                                    continue;
                                }
                                var wall =
                                    GameObjects.AllGameObjects.FirstOrDefault(
                                        i =>
                                        i.IsValid
                                        && Regex.IsMatch(i.Name, "_w_windwall_enemy_0.\\.troy", RegexOptions.IgnoreCase));
                                if (wall == null)
                                {
                                    break;
                                }
                                var wallWidth = 300 + 50 * Convert.ToInt32(wall.Name.Substring(wall.Name.Length - 6, 1));
                                var wallDirection =
                                    (wall.Position.ToVector2() - yasuoWallCastedPos).Normalized().Perpendicular();
                                var wallStart = wall.Position.ToVector2() + wallWidth / 2f * wallDirection;
                                var wallEnd = wallStart - wallWidth * wallDirection;
                                if (
                                    wallStart.Intersection(wallEnd, position.ToVector2(), input.From.ToVector2())
                                        .Intersects)
                                {
                                    var t = Variables.TickCount
                                            + (wallStart.Intersection(
                                                wallEnd,
                                                position.ToVector2(),
                                                input.From.ToVector2()).Point.Distance(input.From) / input.Speed
                                               + input.Delay) * 1000;
                                    if (t < wallCastT + 4000)
                                    {
                                        result.Add(ObjectManager.Player);
                                    }
                                }
                                break;
                        }
                    }
                }
                return result.Distinct().ToList();
            }

            private static void OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
            {
                if (sender.IsValid && sender.Team != ObjectManager.Player.Team && args.SData.Name == "YasuoWMovingWall")
                {
                    wallCastT = Variables.TickCount;
                    yasuoWallCastedPos = sender.ServerPosition.ToVector2();
                }
            }

            #endregion
        }

        private static class OnProcessSpellDetection
        {
            #region Static Fields

            private static readonly List<StoredAutoAttackTime> StoredAutoAttackTimeList =
                new List<StoredAutoAttackTime>();

            #endregion

            #region Constructors and Destructors

            static OnProcessSpellDetection()
            {
                Obj_AI_Base.OnProcessSpellCast += OnProcessSpellCast;
            }

            #endregion

            #region Methods

            internal static double GetLastAutoAttackTime(Obj_AI_Base unit)
            {
                var findTime = StoredAutoAttackTimeList.FirstOrDefault(i => i.NetworkId == unit.NetworkId);
                return findTime == null ? 1 : findTime.Time;
            }

            private static void OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
            {
                var caster = sender as Obj_AI_Hero;
                if (!caster.IsValid() || !AutoAttack.IsAutoAttack(args.SData.Name))
                {
                    return;
                }
                var findTime = StoredAutoAttackTimeList.FirstOrDefault(i => i.NetworkId == sender.NetworkId);
                if (findTime == null)
                {
                    StoredAutoAttackTimeList.Add(
                        new StoredAutoAttackTime { NetworkId = sender.NetworkId, Tick = Variables.TickCount });
                }
                else
                {
                    findTime.Tick = Variables.TickCount;
                }
            }

            #endregion
        }

        internal class PredictionInput
        {
            #region Fields

            public CollisionableObjects[] CollisionObjects =
                {
                    CollisionableObjects.Minions,
                    CollisionableObjects.YasuoWall
                };

            private Vector3 @from;

            private float radius = 1f;

            private float range = float.MaxValue;

            private Vector3 rangeCheckFrom;

            private float speed = float.MaxValue;

            private SkillshotType type = SkillshotType.SkillshotLine;

            private Obj_AI_Base unit = ObjectManager.Player;

            private bool useBoundingRadius = true;

            #endregion

            #region Public Properties

            public bool AoE { get; set; }

            public bool Collision { get; set; }

            public float Delay { get; set; }

            public Vector3 From
            {
                get
                {
                    return this.@from.IsValid() ? this.@from : ObjectManager.Player.ServerPosition;
                }
                set
                {
                    this.@from = value;
                }
            }

            public float Radius
            {
                get
                {
                    return this.radius;
                }
                set
                {
                    this.radius = value;
                }
            }

            public float Range
            {
                get
                {
                    return this.range;
                }
                set
                {
                    this.range = value;
                }
            }

            public Vector3 RangeCheckFrom
            {
                get
                {
                    return this.rangeCheckFrom.IsValid()
                               ? this.rangeCheckFrom
                               : (this.From.IsValid() ? this.From : ObjectManager.Player.ServerPosition);
                }
                set
                {
                    this.rangeCheckFrom = value;
                }
            }

            public float Speed
            {
                get
                {
                    return this.speed;
                }
                set
                {
                    this.speed = value;
                }
            }

            public SkillshotType Type
            {
                get
                {
                    return this.type;
                }
                set
                {
                    this.type = value;
                }
            }

            public Obj_AI_Base Unit
            {
                get
                {
                    return this.unit;
                }
                set
                {
                    this.unit = value;
                }
            }

            public bool UseBoundingRadius
            {
                get
                {
                    return this.useBoundingRadius;
                }

                set
                {
                    this.useBoundingRadius = value;
                }
            }

            #endregion

            #region Properties

            internal float RealRadius
            {
                get
                {
                    return this.Radius + (this.UseBoundingRadius ? this.Unit.BoundingRadius : 0);
                }
            }

            #endregion
        }

        internal class PredictionOutput
        {
            #region Fields

            private List<Obj_AI_Hero> aoeTargetsHit = new List<Obj_AI_Hero>();

            private Vector3 castPosition;

            private List<Obj_AI_Base> collisionObjects = new List<Obj_AI_Base>();

            private HitChance hitChance = HitChance.Impossible;

            private Vector3 unitPosition;

            #endregion

            #region Public Properties

            public int AoeHitCount { get; set; }

            public List<Obj_AI_Hero> AoeTargetsHit
            {
                get
                {
                    return this.aoeTargetsHit;
                }
                set
                {
                    this.aoeTargetsHit = value;
                }
            }

            public int AoeTargetsHitCount
            {
                get
                {
                    return Math.Max(this.AoeHitCount, this.AoeTargetsHit.Count);
                }
            }

            public Vector3 CastPosition
            {
                get
                {
                    return this.castPosition.IsValid() ? this.castPosition.SetZ() : this.Input.Unit.ServerPosition;
                }
                set
                {
                    this.castPosition = value;
                }
            }

            public List<Obj_AI_Base> CollisionObjects
            {
                get
                {
                    return this.collisionObjects;
                }
                set
                {
                    this.collisionObjects = value;
                }
            }

            public HitChance Hitchance
            {
                get
                {
                    return this.hitChance;
                }
                set
                {
                    this.hitChance = value;
                }
            }

            public Vector3 UnitPosition
            {
                get
                {
                    return this.unitPosition.IsValid() ? this.unitPosition.SetZ() : this.Input.Unit.ServerPosition;
                }
                set
                {
                    this.unitPosition = value;
                }
            }

            #endregion

            #region Properties

            internal PredictionInput Input { get; set; }

            #endregion
        }

        private class StoredAutoAttackTime
        {
            #region Properties

            internal int NetworkId { get; set; }

            internal int Tick { private get; set; }

            internal double Time
            {
                get
                {
                    return (Variables.TickCount - this.Tick) / 1000d;
                }
            }

            #endregion
        }
    }
}