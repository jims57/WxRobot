using System;
using System.Collections.Generic;
using Models;
using Models.Commons;
using Models.Https;
using Models.PublicAccounts;
using Models.WX.MessageModel.PreviewMessage;
using Models.WX.ResponseModel;
using System.Web.Configuration;

namespace WXRobot
{
    public partial class Program
    {
        private static SendWXPreviewMessageResponseModel SendPreviewMessage(
            string apiBaseUrl,
            string urlForSendPreviewMessage,
            string dataForSendPreviewMessage,
            Proc_GetMessageInfoByMessageIDs_Result message,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq,
            int showCoverInContent,
            int showSourceUrlInContent,
            int relogin)
        {
            var sendWXPreviewMessageResponseModel = new SendWXPreviewMessageResponseModel() { Ret = -1000 }; //默认让错误码为 -1000，这样当返回为0时，就是成功的
            urlForSendPreviewMessage = string.Format("{0}/api/wx/SendWXPreviewMessage", apiBaseUrl);  //fuxily
            string sourceUrl = null;
            if (!string.IsNullOrEmpty(mq.MoreParams) && !string.IsNullOrEmpty(message.SourceUrl))
            {
                if (message.MessageFunctionID != 12)
                {
                    string secret = WebConfigurationManager.AppSettings["AESEncryptSecret"].ToString();
                    string newSD = "";
                    try
                    {
                        newSD = Common.Encryptor.AESEncryptor.AESEncrypt(mq.ID.ToString(), secret);
                        newSD = System.Web.HttpUtility.UrlEncode(newSD);
                    }
                    catch
                    {

                    }
                    sourceUrl = message.SourceUrl + "?messageQueueID=" + newSD;
                }
                else
                {
                    sourceUrl = message.SourceUrl + ",messageQueueID@" + mq.ID + ",ToUserID@" + mq.ToUserID + "," + mq.MoreParams;
                }
            }
            else
            {
                sourceUrl = message.SourceUrl;
            }
            dataForSendPreviewMessage = Common.HTTP.PostDataHandler.GetPostData
                (
                    new List<PostDataRequestModel>() 
                                        {
                                               new PostDataRequestModel(){ParameterName = "schoolPublicAccountID",ParameterValue= message.SchoolPublicAccountID},
                                               new PostDataRequestModel(){ParameterName = "messageFunctionID",ParameterValue= message.MessageFunctionID},
                                               new PostDataRequestModel(){ParameterName = "toWxNo",ParameterValue= mq.PushNo},
                                               new PostDataRequestModel(){ParameterName = "title",ParameterValue= message.Title},
                                               new PostDataRequestModel(){ParameterName = "digest",ParameterValue= message.Digest},
                                               new PostDataRequestModel(){ParameterName = "content",ParameterValue= message.Content,ConvertToHtml = true},
                                               new PostDataRequestModel(){ParameterName = "sourceUrl",ParameterValue= sourceUrl},
                                               new PostDataRequestModel(){ParameterName = "author",ParameterValue= "志酷通"},
                                               new PostDataRequestModel(){ParameterName = "showCoverPic",ParameterValue= showCoverInContent},
                                               new PostDataRequestModel(){ParameterName = "showSourceUrl",ParameterValue= showSourceUrlInContent},
                                               new PostDataRequestModel(){ParameterName = "relogin",ParameterValue= relogin},
                                               new PostDataRequestModel(){ParameterName = "greeting",ParameterValue= mq.Greeting},
                                               new PostDataRequestModel(){ParameterName = "signature",ParameterValue= mq.NickName},
                                               new PostDataRequestModel(){ParameterName = "toUserID",ParameterValue= mq.ToUserID},
                                               new PostDataRequestModel(){ParameterName = "fromUserID",ParameterValue= mq.FromUserID}
                                        }
                );

            var sendPreviewMessageResultString = _httper.Post(urlForSendPreviewMessage, dataForSendPreviewMessage);

            sendWXPreviewMessageResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<SendWXPreviewMessageResponseModel>(sendPreviewMessageResultString);

            return sendWXPreviewMessageResponseModel;
        }

        /// <summary>
        /// 删除微信公众平台的Widget。【注：只是删除Widget】
        /// </summary>
        /// <param name="publicAccountID"></param>
        /// <param name="AppMsgID"></param>
        /// <param name="messageQueueID"></param>
        /// <param name="apiBaseUrl"></param>
        private static WXCommonResponseModel DeleteWXPreviewMessageWidget(int publicAccountID, int AppMsgID, string apiBaseUrl)
        {
            #region 删除此豆腐块，防止太多豆腐块产生
            var urlForDeleteWidget = string.Format("{0}/api/wx/DeleteWXPreviewMessageWidget", apiBaseUrl);  //fuxily
            string dataForDeleteWidget = string.Format(@"publicAccountID={0}&appMsgID={1}",
                                                        publicAccountID,
                                                        AppMsgID
                                                       );

            var deleteWXPreviewMessageWidgetString = _httper.Post(urlForDeleteWidget, dataForDeleteWidget);
            var deleteWXPreviewMessageWidgetResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<WXCommonResponseModel>(deleteWXPreviewMessageWidgetString);
            #endregion

            return deleteWXPreviewMessageWidgetResponseModel;
        }

        /// <summary>
        /// 删除微信公众平台的Widget和更新数据库Widget状态。
        /// </summary>
        /// <param name="publicAccountID"></param>
        /// <param name="AppMsgID"></param>
        /// <param name="messageQueueID"></param>
        /// <param name="apiBaseUrl"></param>
        private static void DeleteWXPreviewMessageWidgetAndUpdateDBWidgetStatusToDelete(int publicAccountID, int AppMsgID, long messageQueueID, string apiBaseUrl)
        {
            //如果AppMsgID为0时，直接设置为删除状态，没有意义
            if (AppMsgID == 0)
            {
                UpdateMessageQueueWidgetToDeleteStatus(apiBaseUrl, messageQueueID);
            }
            else
            {
                #region 如果
                #region 删除此豆腐块，防止太多豆腐块产生
                var deleteWXPreviewMessageWidgetResponseModel = DeleteWXPreviewMessageWidget(publicAccountID, AppMsgID, apiBaseUrl);
                #endregion

                //如果是空值，说明是可能是网络问题导致的，空值时，不更新数据库，这样，下次，可能会再次取这个消息队列，以确保不会因为网络问题，导致的错过删除
                #region 标记对应的消息列队中的Widget状态为true，说明消息列队在微信公众平台中，对应的AppMsgID块，已经被删除了【-1表示已经被删除的，也要在数据库中标识为删除。出现-1，说明微信公众平台中的Widget已经删除，但平台的数据库，还没有同步。可能是异常导致】
                if (deleteWXPreviewMessageWidgetResponseModel != null &&
                    (deleteWXPreviewMessageWidgetResponseModel.Ret == 0 || deleteWXPreviewMessageWidgetResponseModel.Ret == -1)
                )
                {
                    UpdateMessageQueueWidgetToDeleteStatus(apiBaseUrl, messageQueueID);
                }
                #endregion
                #endregion
            }
        }

        private static IEnumerable<WXPublicAccountBlockStatusModel> SetPreviewMessageSuccess(string apiBaseUrl, int serverID,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq,
           SendWXPreviewMessageResponseModel sendWXPreviewMessageResponseModel,
            IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatuses)
        {
            #region 状态此消息队列状态为“已发送”【注：此接口，已经自动把公众账号修改为Unblocked状态，所以不用专门更新Unblock状态，以减少跟数据库的请求次数】
            var dataForUpdateMessageQueueStatusToSent = string.Format("serverID={0}&messageQueueIDs={1}&AppMsgIDs={2}", serverID, mq.ID, sendWXPreviewMessageResponseModel.AppMsgID);
            var updateMessageQueueStatusToSentString = _httper.Post(string.Format("{0}/api/zcooApi/UpdateMessageQueueStatusToSent", apiBaseUrl), dataForUpdateMessageQueueStatusToSent);

            //得到返回Model
            var updateMessageQueueStatusToSentResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updateMessageQueueStatusToSentString);
            mq.ShouldDelete = true;
            #endregion

            #region 成功时，把公众账号改回Unblocked状态
            if (updateMessageQueueStatusToSentResponseModel != null &&
                string.Compare(updateMessageQueueStatusToSentResponseModel.ReturnMessage, "OK", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                wxPublicAccountBlockStatuses = UpdateWXPublicAccountBlockStatusModelToUnblockStatus(wxPublicAccountBlockStatuses, (int)mq.PublicAccountID);
            }
            #endregion

            return wxPublicAccountBlockStatuses;
        }

        /// <summary>
        /// 设置消息为无效推送号。
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="mq"></param>
        /// <returns></returns>
        private static CommonResponseModel SetPreviewMessageInvalidPushNo(
            string apiBaseUrl,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq
            )
        {
            var urlForUpdateMessageQueueToInvalidStatus = string.Format("{0}/api/zcooApi/UpdateMessageQueueToInvalidStatus", apiBaseUrl);
            string dataForUpdateMessageQueueToInvalidStatus = Common.HTTP.PostDataHandler.GetPostData
                (
                    new List<PostDataRequestModel>() 
                                            {
                                                   new PostDataRequestModel(){ParameterName = "MessageQueueID",ParameterValue = mq.ID,ConvertToHtml = false},
                                                   new PostDataRequestModel(){ParameterName = "InvalidReason",ParameterValue = string.Format("{0}此无效的微信号是：{1}",Common.Const.Zcoo.ZcooConst.INVALID_PUSH_NO_WHEN_SEND_PREVIEW_MESSAGE,mq.PushNo),ConvertToHtml =false}
                                            }
                );

            var updateMessageQueueToInvalidStatusResultString = _httper.Post(urlForUpdateMessageQueueToInvalidStatus, dataForUpdateMessageQueueToInvalidStatus);

            var updateMessageQueueToInvalidStatusResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updateMessageQueueToInvalidStatusResultString);

            return updateMessageQueueToInvalidStatusResponseModel;
        }
    }
}
