<%@ Page Language="C#" %>
<%-- Issue #27: with relaxedUrlToFileSystemMapping="true" (Web.config), every MapPath returned
     <site>/NOT_A_VALID_FILESYSTEM_PATH on Linux, because '/' was one of the characters that make a mapped path invalid. --%>
mapped: <%= System.IO.File.Exists(Server.MapPath("~/Default.aspx")) %>
