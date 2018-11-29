using Models;
using Models.Commons;
using Models.Https;
using Models.ShortMessage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Configuration;

namespace WXRobot
{
    public partial class Program
    {
        /// <summary>
        /// 发送短信
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="message"></param>
        /// <param name="mq"></param>
        /// <returns></returns>
        public static ShortMessageResponseModel SendShortMessage(string apiBaseUrl,
            Proc_GetMessageInfoByMessageIDs_Result message,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq)
        {
            //短信宝帐号类型ID
            int accountID = Convert.ToInt32(WebConfigurationManager.AppSettings["SMSAccountID"].ToString());
            ShortMessageResponseModel shortMessageResponseModel = new ShortMessageResponseModel() { ReturnCode = -1 };
            try
            {
                string urlForSendShortMessage = string.Format("{0}/api/zcooapi/SendShortMessage", apiBaseUrl);
                //消息的类型名
                string title = message.Title.Substring(message.Title.IndexOf("《") + 1, message.Title.LastIndexOf("》") - 1);
                title = "【" + title + "】";
                string Content = message.Content;
                string Sendname = "(" + mq.NickName + ")"; //发送消息者
                string greeting = "尊敬的家长";
                if (mq.MoreParams.Contains("isSendToSMSUser"))
                {
                    greeting = "尊敬的用户";
                }
                Content = Regex.Replace(Content, @"<\/?[^>]*>", ""); //去除HTML tag
                Content = Regex.Replace(Content, "((http[s]{0,1}|ftp)://[a-zA-Z0-9\\.\\-]+\\.([a-zA-Z]{2,4})(:\\d+)?(/[a-zA-Z0-9\\.\\-~!@#$%^&*+?:_/=<>]*)?)|(www.[a-zA-Z0-9\\.\\-]+\\.([a-zA-Z]{2,4})(:\\d+)?(/[a-zA-Z0-9\\.\\-~!@#$%^&*+?:_/=<>]*)?)", "【链接】");
                #region 转化为短链接 暂停使用
                //if (Content.Length > 35) //35
                //{
                //    string sd = mq.ID.ToString();
                //    string secret = WebConfigurationManager.AppSettings["AESEncryptSecret"].ToString();
                //    sd = Common.Encryptor.AESEncryptor.AESEncrypt(sd, secret);
                //    //需要编码三次。。。。醉了
                //    sd = System.Web.HttpUtility.UrlEncode(sd);
                //    sd = System.Web.HttpUtility.UrlEncode(sd);
                //    string messageURL = string.Format("{0}/html5/ShowDetail?sd={1}", apiBaseUrl, sd);
                //    messageURL = System.Web.HttpUtility.UrlEncode(messageURL);
                //    string GETshorURL = string.Format("{0}/api/ZcooApi/getShortUrl?Ursl={1}", apiBaseUrl, messageURL);
                //    string shorURL = _httper.Get(GETshorURL);
                //    Content = Content.Substring(0, 9) + "..." + "\n" + "更多内容：" + shorURL;
                //}
                #endregion

                Content = "\n" + greeting + "，" + Content + "\n";
                if (title.Contains("学习成长"))
                {
                    int lenth = Sendname.Length + title.Length;
                    if (Content.Length > 64 - lenth)
                    {
                        string prompt = "[部分消息被截取，请微信关注：zcootong]\n";
                        Content = Content.Substring(0, 64 - lenth - prompt.Length);
                        Content = Content + prompt;
                    }
                }

                string digest = title + Content + Sendname;
                string dataForSendShortMessage = Common.HTTP.PostDataHandler.GetPostData
                   (
                       new List<PostDataRequestModel>() 
                                        {
                                               new PostDataRequestModel(){ParameterName = "accountID",ParameterValue= accountID},
                                               new PostDataRequestModel(){ParameterName = "toMobileNo",ParameterValue= mq.PushNo},
                                               new PostDataRequestModel(){ParameterName = "content",ParameterValue= digest }
                                        }
                   );

                //发送短信消息
                string sendShortMessageResult = _httper.Post(urlForSendShortMessage, dataForSendShortMessage);

                shortMessageResponseModel.ReturnCode = Newtonsoft.Json.JsonConvert.DeserializeObject<int>(sendShortMessageResult);

            }
            catch (Exception)
            {
                shortMessageResponseModel.ReturnCode = -1;
            }

            return shortMessageResponseModel;
        }

        /// <summary>
        /// 当IP地址限制时，设置下次发送时间
        /// </summary>
        /// <param name="messageQueues"></param>
        /// <param name="publicAccountID"></param>
        /// <returns></returns>
        private static IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> SetShortMessageQueueNextSendTime(
            IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues, int addMinutes)
        {
            var messageQueuesList = messageQueues.ToList();
            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();
            var nextSendDataTime = currentDateTime.AddMinutes(addMinutes);

            messageQueues = messageQueuesList.Select(mq =>
            {
                mq.NextSendDateTime = nextSendDataTime;
                return mq;
            });

            return messageQueues;
        }

        /// <summary>
        /// 设置短信消息发送成功
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="serverID"></param>
        /// <param name="mq"></param>
        /// <returns></returns>
        private static CommonResponseModel SetShortMessageSuccess(string apiBaseUrl, int serverID,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq
            )
        {
            Random rand = new Random();
            int appMsgID = Convert.ToInt32(("5" + rand.Next(10000000, 99999999).ToString()));

            //更新消息队列状态
            var dataForUpdateMessageQueueStatusToSent = string.Format("serverID={0}&messageQueueIDs={1}&AppMsgIDs={2}", serverID, mq.ID, appMsgID);
            var updateMessageQueueStatusToSentString = _httper.Post(string.Format("{0}/api/zcooApi/UpdateMessageQueueStatusToSent", apiBaseUrl), dataForUpdateMessageQueueStatusToSent);

            var updateMessageQueueStatusToSentResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updateMessageQueueStatusToSentString);
            mq.ShouldDelete = true;

            return updateMessageQueueStatusToSentResponseModel;
        }

        /// <summary>
        /// 设置消息为无效推送号。
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="mq"></param>
        /// <returns></returns>
        private static CommonResponseModel SetShortMessageInvalidPushNo(
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
                                                   new PostDataRequestModel(){ParameterName = "InvalidReason",ParameterValue = string.Format("{0}此无效的Mobile是：{1}",Common.Const.Zcoo.ZcooConst.INVALID_PUSH_NO_WHEN_SEND_SHORT_MESSAGE,mq.PushNo),ConvertToHtml =false}
                                            }
                );

            var updateMessageQueueToInvalidStatusResultString = _httper.Post(urlForUpdateMessageQueueToInvalidStatus, dataForUpdateMessageQueueToInvalidStatus);

            var updateMessageQueueToInvalidStatusResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updateMessageQueueToInvalidStatusResultString);

            return updateMessageQueueToInvalidStatusResponseModel;
        }
    }
}
