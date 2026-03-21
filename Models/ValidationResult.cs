using System.Collections.Generic;

namespace Renumber.Models
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Messages { get; set; } = new List<string>();

        public void AddError(string message)
        {
            IsValid = false;
            Messages.Add($"[Error] {message}");
        }

        public void AddSuccess(string message)
        {
            Messages.Add($"[Success] {message}");
        }
    }
}
