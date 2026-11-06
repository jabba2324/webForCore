using System;
using System.Data.Linq;
using System.Data.SqlClient;
using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using MvcSample.Models;

namespace MvcSample.Controllers
{
    // Optimistic concurrency with a rowversion, as MVC 5 applications on LINQ to SQL do it: the edit form carries the
    // row's Version (System.Data.Linq.Binary) in a hidden input, and the post binds it back (ModelBinders' default
    // LinqBinaryModelBinder) and updates with it, so that a row changed in between is a ChangeConflictException.
    public class NotesController : Controller
    {
        // Recreates the database (Web.config's Notes connection string) with one note.
        public ActionResult Reset()
        {
            var builder = new SqlConnectionStringBuilder(ConfigurationManager.ConnectionStrings["Notes"].ConnectionString);
            string database = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            using (var connection = new SqlConnection(builder.ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("IF DB_ID('" + database + "') IS NOT NULL BEGIN ALTER DATABASE [" + database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + database + "] END; CREATE DATABASE [" + database + "]", connection))
                    command.ExecuteNonQuery();
            }
            SqlConnection.ClearAllPools();
            using (var context = new NotesDataContext())
            {
                context.ExecuteCommand("CREATE TABLE dbo.Notes (Id int PRIMARY KEY, Text nvarchar(100) NOT NULL, Version rowversion NOT NULL); INSERT dbo.Notes (Id, Text) VALUES (1, N'first')");
            }
            return Content("reset", "text/plain");
        }

        public ActionResult Edit(int id)
        {
            using (var context = new NotesDataContext())
            {
                return View(context.Notes.Single(n => n.Id == id));
            }
        }

        // The Binary bound from the form, the version check against the database, and the new version.
        [HttpPost]
        public ActionResult Edit(Note note, Binary check)
        {
            string bound = (note.Version == null ? "null" : note.Version.GetType().FullName + "[" + note.Version.Length + "]")
                + " check=" + (check != null && check.Equals(note.Version));
            using (var context = new NotesDataContext())
            {
                context.Notes.Attach(note, true);
                try
                {
                    context.SubmitChanges();
                    return Content(bound + " saved " + note.Text + " version changed=" + (!note.Version.Equals(check)), "text/plain");
                }
                catch (ChangeConflictException e)
                {
                    return Content(bound + " conflict: " + e.Message, "text/plain");
                }
            }
        }
    }
}
