namespace MvcSample.Models
{
    // A view model an application keeps in TempData, as MVCForum's GenericMessageViewModel: not [Serializable]. In-process
    // session state (<sessionState mode="InProc">, the default) keeps the object itself, so it needn't be (#180).
    public class GenericMessageViewModel
    {
        public string Message { get; set; }
    }
}
