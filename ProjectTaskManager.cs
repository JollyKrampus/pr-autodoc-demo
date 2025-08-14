using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Demo
{
    /// <summary>
    /// Represents a simple task manager with projects and tasks.
    /// Includes features for adding, removing, updating, and querying tasks.
    /// This is intentionally verbose to exceed 100 lines.
    /// </summary>
    public class ProjectTaskManager
    {
        private readonly Dictionary<string, List<TaskItem>> _projects;

        public ProjectTaskManager()
        {
            _projects = new Dictionary<string, List<TaskItem>>(StringComparer.OrdinalIgnoreCase);
        }

        public void AddProject(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("Project name cannot be empty.");

            if (!_projects.ContainsKey(projectName))
                _projects[projectName] = new List<TaskItem>();
        }

        public void RemoveProject(string projectName)
        {
            if (_projects.ContainsKey(projectName))
                _projects.Remove(projectName);
        }

        public void AddTask(string projectName, string taskName, DateTime dueDate)
        {
            if (!_projects.ContainsKey(projectName))
                throw new InvalidOperationException("Project does not exist.");

            var task = new TaskItem
            {
                Name = taskName,
                DueDate = dueDate,
                Status = TaskStatus.Pending
            };

            _projects[projectName].Add(task);
        }

        public void CompleteTask(string projectName, string taskName)
        {
            var task = FindTask(projectName, taskName);
            if (task != null)
                task.Status = TaskStatus.Completed;
        }

        public void ReopenTask(string projectName, string taskName)
        {
            var task = FindTask(projectName, taskName);
            if (task != null)
                task.Status = TaskStatus.Pending;
        }

        public void RemoveTask(string projectName, string taskName)
        {
            if (_projects.ContainsKey(projectName))
            {
                _projects[projectName] = _projects[projectName]
                    .Where(t => !string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        public IEnumerable<TaskItem> GetAllTasks(string projectName)
        {
            if (_projects.ContainsKey(projectName))
                return _projects[projectName];
            return Enumerable.Empty<TaskItem>();
        }

        public IEnumerable<TaskItem> GetOverdueTasks(string projectName)
        {
            if (_projects.ContainsKey(projectName))
                return _projects[projectName].Where(t => t.DueDate < DateTime.Now && t.Status != TaskStatus.Completed);
            return Enumerable.Empty<TaskItem>();
        }

        public IEnumerable<TaskItem> GetCompletedTasks(string projectName)
        {
            if (_projects.ContainsKey(projectName))
                return _projects[projectName].Where(t => t.Status == TaskStatus.Completed);
            return Enumerable.Empty<TaskItem>();
        }

        public void PrintSummary()
        {
            Console.WriteLine("=== Project Summary ===");
            foreach (var project in _projects)
            {
                Console.WriteLine($"Project: {project.Key}");
                foreach (var task in project.Value)
                {
                    Console.WriteLine($"  - {task.Name} [{task.Status}] Due: {task.DueDate:yyyy-MM-dd}");
                }
            }
        }

        private TaskItem FindTask(string projectName, string taskName)
        {
            if (_projects.ContainsKey(projectName))
            {
                return _projects[projectName]
                    .FirstOrDefault(t => string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        public class TaskItem
        {
            public string Name { get; set; }
            public DateTime DueDate { get; set; }
            public TaskStatus Status { get; set; }
        }

        public enum TaskStatus
        {
            Pending,
            Completed
        }
    }

    class Program
    {
        static void Main()
        {
            var manager = new ProjectTaskManager();
            manager.AddProject("Home Renovation");
            manager.AddTask("Home Renovation", "Paint living room", DateTime.Now.AddDays(7));
            manager.AddTask("Home Renovation", "Install new lights", DateTime.Now.AddDays(3));
            manager.CompleteTask("Home Renovation", "Paint living room");
            manager.PrintSummary();
        }
    }
}
