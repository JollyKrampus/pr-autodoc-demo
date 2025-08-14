using System;
using System.Collections.Generic;

namespace BugTrackerExample
{
    // Interface that all bug reporters must implement
    public interface IBugReporter
    {
        string ReporterName { get; }
        void ReportBug(Bug bug);
    }

    // Interface for bug lifecycle management
    public interface IBugLifecycle
    {
        void Assign(string developer);
        void Resolve(string resolutionNotes);
        void Close();
    }

    // Abstract base class for a bug
    public abstract class Bug : IBugLifecycle
    {
        public int Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string AssignedTo { get; private set; }
        public string Status { get; private set; }
        public string ResolutionNotes { get; private set; }

        protected Bug(int id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
            Status = "New";
        }

        public virtual void Assign(string developer)
        {
            AssignedTo = developer;
            Status = "Assigned";
            Console.WriteLine($"Bug {Id} assigned to {developer}.");
        }

        public virtual void Resolve(string resolutionNotes)
        {
            ResolutionNotes = resolutionNotes;
            Status = "Resolved";
            Console.WriteLine($"Bug {Id} resolved: {resolutionNotes}");
        }

        public void Close()
        {
            if (Status != "Resolved")
            {
                Console.WriteLine($"Bug {Id} cannot be closed until resolved.");
                return;
            }
            Status = "Closed";
            Console.WriteLine($"Bug {Id} is now closed.");
        }

        // Abstract method - must be implemented by subclasses
        public abstract void PrintDetails();
    }

    // A specific kind of bug - UI Bug
    public class UiBug : Bug
    {
        public string AffectedScreen { get; }

        public UiBug(int id, string title, string description, string affectedScreen)
            : base(id, title, description)
        {
            AffectedScreen = affectedScreen;
        }

        public override void PrintDetails()
        {
            Console.WriteLine($"[UI BUG] #{Id} - {Title}");
            Console.WriteLine($"Description: {Description}");
            Console.WriteLine($"Affected Screen: {AffectedScreen}");
            Console.WriteLine($"Status: {Status}, Assigned To: {AssignedTo}");
            Console.WriteLine();
        }
    }

    // Another kind of bug - Backend Bug
    public class BackendBug : Bug
    {
        public string AffectedService { get; }

        public BackendBug(int id, string title, string description, string service)
            : base(id, title, description)
        {
            AffectedService = service;
        }

        public override void Assign(string developer)
        {
            // Special behavior: backend bugs require a senior developer
            if (!developer.Contains("Senior"))
            {
                Console.WriteLine($"Backend bug #{Id} must be assigned to a senior developer.");
                return;
            }
            base.Assign(developer);
        }

        public override void PrintDetails()
        {
            Console.WriteLine($"[BACKEND BUG] #{Id} - {Title}");
            Console.WriteLine($"Description: {Description}");
            Console.WriteLine($"Affected Service: {AffectedService}");
            Console.WriteLine($"Status: {Status}, Assigned To: {AssignedTo}");
            Console.WriteLine();
        }
    }

    // A class that implements IBugReporter
    public class Developer : IBugReporter
    {
        public string ReporterName { get; }

        public Developer(string name)
        {
            ReporterName = name;
        }

        public void ReportBug(Bug bug)
        {
            Console.WriteLine($"{ReporterName} reported bug #{bug.Id}: {bug.Title}");
            bug.PrintDetails();
        }
    }

    class Program
    {
        static void Main()
        {
            IBugReporter alice = new Developer("Alice");
            IBugReporter bob = new Developer("Bob Senior");

            Bug uiBug = new UiBug(101, "Button not clickable", "The submit button is unresponsive.", "Checkout Page");
            Bug backendBug = new BackendBug(202, "Data mismatch", "User data not syncing properly.", "User Service");

            alice.ReportBug(uiBug);
            bob.ReportBug(backendBug);

            uiBug.Assign("Junior Dev");
            backendBug.Assign("Bob Senior");

            uiBug.Resolve("Fixed the button's onclick handler.");
            backendBug.Resolve("Fixed data sync logic in the microservice.");

            uiBug.Close();
            backendBug.Close();
        }
    }
}
