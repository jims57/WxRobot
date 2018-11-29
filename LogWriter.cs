using Models.Commons;
using Models.Enum.Apps;
using Models.Enum.Errors;
using Models.Https;
using System;
using System.Collections.Generic;
using System.Web.Configuration;

namespace WXRobot
{
    public static class LogWriter
    {
        public static CommonResponseModel WriteLogToDBBySuperAdmin(int messageFunctionType, string function, int severityLevelType, string errorMessage)
        {
            string apiBaseUrl = WebConfigurationManager.AppSettings["APIBaseUrl"].ToString();

            var urlForWriteLogToDBBySuperAdmin = string.Format("{0}/api/zcooApi/WriteLogToDBBySuperAdmin", apiBaseUrl);

            string dataForWriteLogToDBBySuperAdmin = Common.HTTP.PostDataHandler.GetPostData
                (
                    new List<PostDataRequestModel>() 
                        {       
                            new PostDataRequestModel(){ParameterName = "AppType",ParameterValue= (int)AppType.WXRobot},
                            new PostDataRequestModel(){ParameterName = "messageFunctionType",ParameterValue= messageFunctionType},
                            new PostDataRequestModel(){ParameterName = "function",ParameterValue= function},
                            new PostDataRequestModel(){ParameterName = "severityLevelType",ParameterValue= severityLevelType},
                            new PostDataRequestModel(){ParameterName = "errorMessage",ParameterValue= errorMessage}
                        }
                );

            var writeLogToDBBySuperAdminResultString = Program.Httper.Post(urlForWriteLogToDBBySuperAdmin, dataForWriteLogToDBBySuperAdmin);
            Models.Commons.CommonResponseModel writeLogToDBBySuperAdminResponseModel = new CommonResponseModel();
            try
            {
                writeLogToDBBySuperAdminResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(writeLogToDBBySuperAdminResultString);
                return writeLogToDBBySuperAdminResponseModel;
            }
            catch(Exception e)
            {
                writeLogToDBBySuperAdminResponseModel.ErrorCode = ErrorCode.UnknownException;
                writeLogToDBBySuperAdminResponseModel.ReturnMessage = e.Message;
                return writeLogToDBBySuperAdminResponseModel;
            }
        }
    }
}
