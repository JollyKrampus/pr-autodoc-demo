using System;
using System.Collections.Generic;

namespace InsectWorld
{
    // Simple capability interfaces
    public interface IFlyer
    {
        Wing LeftWing { get; }
        Wing RightWing { get; }
        void TakeOff();
        void Land();
        void Fly(int meters);
    }

    public interface IDigger
    {
        int MaxDepthCm { get; }
        void Dig(int centimeters);
    }

    public interface ISinger
    {
        string Song { get; }
        void Sing();
    }

    // A lightweight immutable value type for wings
    public readonly record struct Wing(string Type, double SpanCm);

    // Base abstract insect with some common state/behavior
    public abstract class Insect
    {
        // Demonstrate required + init + readonly field via a backing private set
        public required string Name { get; init; }
        public int Legs { get; init; } = 6; // common default
        protected internal InsectSize Size { get; init; } = InsectSize.Medium; // protected internal

        // Virtual so derived types can customize motion styles
        public virtual void Move()
        {
            Console.WriteLine($"{Name} scuttles along.");
        }

        // Template method pattern
        public void DailyRoutine()
        {
            Console.WriteLine($"-- {Name}'s day begins --");
            Forage();
            Rest();
            Console.WriteLine($"-- {Name}'s day ends --");
        }

        protected virtual void Forage() => Console.WriteLine($"{Name} forages for food.");
        protected virtual void Rest() => Console.WriteLine($"{Name} rests under a leaf.");
        public override string ToString() => $"{Name} ({GetType().Name}, {Size})";
    }

    public enum InsectSize { Tiny, Small, Medium, Large }

    // Abstract specialization for flyers
    public abstract class FlyingInsect : Insect, IFlyer
    {
        public Wing LeftWing { get; init; } = new Wing("Membranous", 2.0);
        public Wing RightWing { get; init; } = new Wing("Membranous", 2.0);

        public virtual void TakeOff() => Console.WriteLine($"{Name} takes off gracefully.");
        public virtual void Land() => Console.WriteLine($"{Name} lands on a twig.");
        public virtual void Fly(int meters) => Console.WriteLine($"{Name} flies {meters} meters.");

        // Override to prefer air movement
        public override void Move()
        {
            TakeOff();
            Fly(5);
            Land();
        }
    }

    // Abstract specialization for diggers
    public abstract class DiggingInsect : Insect, IDigger
    {
        public int MaxDepthCm { get; init; } = 50;
        public virtual void Dig(int centimeters)
        {
            var depth = Math.Min(centimeters, MaxDepthCm);
            Console.WriteLine($"{Name} digs {depth} cm underground.");
        }

        public override void Move()
        {
            Console.WriteLine($"{Name} chooses to move via shallow tunnels.");
        }

        protected override void Forage()
        {
            Console.WriteLine($"{Name} forages beneath the soil.");
        }
    }

    // Concrete flyers
    public sealed class Bee : FlyingInsect, ISinger
    {
        public const string Species = "Apis mellifera";
        public string Song => "Bzzzz";

        // Sealed override: derived types cannot change this again
        public sealed override void TakeOff()
        {
            Console.WriteLine($"{Name} (bee) rockets upward with a buzz.");
        }

        public void Sing() => Console.WriteLine($"{Name} sings: {Song}");

        protected override void Forage()
        {
            Console.WriteLine($"{Name} gathers nectar and pollen.");
        }
    }

    public sealed class Dragonfly : FlyingInsect
    {
        public override void Fly(int meters)
        {
            Console.WriteLine($"{Name} darts {meters} meters at high speed, mid-air braking enabled.");
        }

        protected override void Rest()
        {
            Console.WriteLine($"{Name} perches on a reed, compound eyes scanning.");
        }
    }

    // Concrete diggers
    public class Ant : DiggingInsect, ISinger
    {
        // Explicit interface implementation to show off the syntax
        string ISinger.Song => "Tik-tik";
        void ISinger.Sing() => Console.WriteLine($"{Name} taps mandibles rhythmically (tik-tik).");

        public override void Move()
        {
            Console.WriteLine($"{Name} marches along pheromone trails.");
        }

        public override void Dig(int centimeters)
        {
            base.Dig(centimeters);
            Console.WriteLine($"{Name} strengthens the tunnel walls with saliva.");
        }
    }

    public sealed class MoleCricket : DiggingInsect, ISinger
    {
        public string Song => "Trill-trill";
        public void Sing() => Console.WriteLine($"{Name} chirps a low subterranean {Song}.");

        protected override void Rest()
        {
            Console.WriteLine($"{Name} rests in a bulb-shaped resonance chamber.");
        }
    }

    // A mixed-role insect (some beetles can both fly and dig)
    public class GroundBeetle : Insect, IFlyer, IDigger
    {
        public Wing LeftWing { get; init; } = new Wing("Elytron+Wing", 3.2);
        public Wing RightWing { get; init; } = new Wing("Elytron+Wing", 3.2);
        public int MaxDepthCm { get; init; } = 15;

        public void TakeOff() => Console.WriteLine($"{Name} opens elytra and takes off.");
        public void Land() => Console.WriteLine($"{Name} tucks wings beneath elytra and lands.");
        public void Fly(int meters) => Console.WriteLine($"{Name} flies a cautious {meters} meters.");
        public void Dig(int centimeters)
        {
            var depth = Math.Min(centimeters, MaxDepthCm);
            Console.WriteLine($"{Name} scrapes soil to {depth} cm.");
        }

        public override void Move()
        {
            Console.WriteLine($"{Name} alternates: runs, digs, then brief flight.");
        }
    }

    // Internal helper for habitats (internal modifier)
    internal static class Habitat
    {
        public static void Describe(Insect insect)
        {
            var environment = insect switch
            {
                IFlyer => "meadows and open air",
                IDigger => "underground burrows",
                _ => "leaf litter"
            };
            Console.WriteLine($"{insect.Name} prefers {environment}.");
        }
    }

    // Extension methods (static class, this modifier)
    public static class InsectExtensions
    {
        public static void PerformShowcase(this Insect insect)
        {
            Console.WriteLine(insect);
            Habitat.Describe(insect);
            insect.Move();

            if (insect is ISinger singer) singer.Sing();
            if (insect is IFlyer flyer)
            {
                flyer.TakeOff();
                flyer.Fly(7);
                flyer.Land();
            }
            if (insect is IDigger digger) digger.Dig(20);

            insect.DailyRoutine();
            Console.WriteLine();
        }
    }

    // Demo
    public static class Program
    {
        public static void Main()
        {
            var insects = new List<Insect>
            {
                new Bee { Name = "Busy Bee", Size = InsectSize.Small, LeftWing = new Wing("Membranous", 1.5), RightWing = new Wing("Membranous", 1.5) },
                new Dragonfly { Name = "Sky Dancer", Size = InsectSize.Medium, LeftWing = new Wing("Membranous", 4.0), RightWing = new Wing("Membranous", 4.0) },
                new Ant { Name = "Tunnel Scout", Size = InsectSize.Tiny },
                new MoleCricket { Name = "Bass Chirper", Size = InsectSize.Small, MaxDepthCm = 80 },
                new GroundBeetle { Name = "Hybrid Rover", Size = InsectSize.Medium }
            };

            foreach (var insect in insects)
            {
                insect.PerformShowcase();
            }
        }
    }
}
