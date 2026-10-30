using System;
using System.Collections.Generic;

namespace MvcSample.Models
{
    // A multi-step form's state, carried between requests in a hidden field by Html.Serialize and read back with
    // [Deserialize] (MVC Futures' MvcSerializer: signed and encrypted with the machine key).
    [Serializable]
    public class WizardState
    {
        public int Step;
        public string Name;
        public List<string> Choices = new List<string>();
        public DateTime Started;
        public Address Shipping;
    }
}
