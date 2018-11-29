using Biz.WX.AccessToken;
using Common.Json;
using Interfaces.WX.AccessToken;
using Models;
using Models.Commons;
using Models.Https;
using Models.PublicAccounts;
using Models.WX.MessageModel.TemplateMessage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Text.RegularExpressions;
using Models.Error;

namespace WXRobot
{
    public partial class Program
    {

        /// <summary>
        /// 发送模板消息
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="message"></param>
        /// <param name="mq"></param>
        /// <returns></returns>
        private static SendWXTemplateMessageResponseModel SendTemplateMessage(
            string apiBaseUrl,
            Proc_GetMessageInfoByMessageIDs_Result message,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq
            )
        {
            SendWXTemplateMessageResponseModel sendWXTemplateMessageResponseModel = null;

            try
            {
                //模板消息post路径
                string urlForSendTemplateMessage = _httper.Get(string.Format("{0}/api/wx/GetTemplateMessageBaseUrl?publicAccountID={1}", apiBaseUrl, mq.PublicAccountID));  //fuxily
                urlForSendTemplateMessage = urlForSendTemplateMessage.Replace("\"", "");
                string TemplateMessageBaseUrl = string.Format("{0}/api/wx/SendWXTemplateMessage", apiBaseUrl);  //fuxily
                int toUserID = mq.ToUserID ?? 0;
                if (mq.MoreParams != null)
                {
                    var morePara = Common.GetUrlParas.GetUrlParasHandler.GetUrlParas(mq.MoreParams);
                    if (morePara != null)
                    {
                        foreach (var item in morePara)
                        {
                            if (item.Key == "copyToUserID")
                            {
                                toUserID = Common.DataTypes.IntHandler.ToInt32(item.Value);
                            }
                        }
                    }
                }



                string TemplateMessageData = Common.HTTP.PostDataHandler.GetPostData
                   (
                       new List<PostDataRequestModel>() 
                                        {
                                               new PostDataRequestModel(){ParameterName = "schoolPublicAccountID",ParameterValue= message.SchoolPublicAccountID},
                                               new PostDataRequestModel(){ParameterName = "messageFunctionID",ParameterValue= message.MessageFunctionID},
                                               new PostDataRequestModel(){ParameterName = "messageID",ParameterValue= message.ID},
                                               new PostDataRequestModel(){ParameterName = "messageQueueID",ParameterValue= mq.ID},
                                               new PostDataRequestModel(){ParameterName = "toUserID",ParameterValue= toUserID},
                                               new PostDataRequestModel(){ParameterName = "fromUserID",ParameterValue= mq.FromUserID}
                                        }
                   );

                //模板消息json数据
                string xmlData = _httper.Post(TemplateMessageBaseUrl, TemplateMessageData);

                if (!string.IsNullOrEmpty(xmlData))
                {
                    //移除xmlData首尾的引号
                    xmlData = xmlData.Remove(0, 1);
                    xmlData = xmlData.Remove(xmlData.Length - 1, 1);
                    //加载XML
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(xmlData);
                    // XmlNode node = doc.SelectSingleNode("xml/data/keyword1");
                    XmlNode contentNode = null;
                    switch (message.MessageFunctionID)
                    {
                        case 1:
                        case 2:
                        case 3:
                        case 4:
                        case 17:
                        case 3000:
                            contentNode = doc.SelectSingleNode("xml/data/keyword3/value");
                            break;
                        default:
                            contentNode = doc.SelectSingleNode("xml/data/keyword1/value");
                            break;
                    } 
                    string content = contentNode.InnerText;
                    //content = Regex.Replace(content, @"\p{Cs}", "");//把手机中Emoji表情字符去掉。解决出现Emoji表情字符时，LoadXml加载失败，导致机器人发送总是返回Null问题（一直发送失败）
                    string replaceContent = content.Replace("\\\"", "”");//把发送信息中的"替换成中文的”
                    content = string.IsNullOrEmpty(content) ? "内容为空" : content;

                    if (replaceContent.Length > 140) //当消息内容超过140字时，截取前140个字，并显示【更多】
                    {
                        replaceContent = replaceContent.Substring(0, 140);
                        replaceContent += "【更多】";
                    }
                    string replaceXml = xmlData.Replace(content, replaceContent);
                    doc.LoadXml(replaceXml);
                    var jsonData = XmlToJson.XmlToJSON(doc);

                    var dataForSendTemplateMessage = jsonData.Remove(jsonData.LastIndexOf("}")).Remove(0, 8);
                    //dataForSendTemplateMessage = ClearImageHtml(dataForSendTemplateMessage);  //清除模版消息中的图片html标签-Haley
                    var sendTemplateMessageResultString = "";
                    lock (sendMessageLock)
                    {
                        var model = messageQueueIDs.Where(
                            p => p.MessageQueueID == mq.ID && (Common.Time.NowHandler.GetNowByTimeZone() - p.Created).TotalSeconds < 60
                        ).FirstOrDefault();
                        //去重，1分钟之内，没有发送过的MessageQueueID才去发送
                        if (model == null)
                        {
                            sendTemplateMessageResultString = _httper.Post(urlForSendTemplateMessage, dataForSendTemplateMessage);
                            sendWXTemplateMessageResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<SendWXTemplateMessageResponseModel>(sendTemplateMessageResultString);
                            if (sendWXTemplateMessageResponseModel.ErrCode == 0)
                            {
                                messageQueueIDs.Add(new Models.MessageQueues.MessageQueueIDModel() { MessageQueueID = mq.ID ?? 0, Created = Common.Time.NowHandler.GetNowByTimeZone() });
                            }
                        }
                    }
                    //删除1分钟后的消息队列ID
                    messageQueueIDs.RemoveAll(
                        p => (Common.Time.NowHandler.GetNowByTimeZone() - p.Created).TotalSeconds > 70
                    );
                    if (!string.IsNullOrEmpty(sendTemplateMessageResultString))
                    {
                        //将json转换成对象
                        if (sendWXTemplateMessageResponseModel.ErrCode == 42001 || //当AccessToken无效时
                           sendWXTemplateMessageResponseModel.ErrCode == 40001)//当AccessToken过期时（有可能刚好过期）)
                        {
                            string[] urls = urlForSendTemplateMessage.Split('=');
                            string invalidToken = urls[1];
                            string urlForRefreshWXAccessToken = string.Format("{0}/api/ZcooApi/RefreshWXAccessToken?publicAccountID={1}&invalidToken={2}", apiBaseUrl, mq.PublicAccountID, invalidToken);
                            _httper.Get(urlForRefreshWXAccessToken);
                        }
                    }
                }

                return sendWXTemplateMessageResponseModel;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("字符串的长度不能为零"))
                {
                    templateMessageQueues.RemoveAll(c => c.MessageID == mq.MessageID);
                    messages.RemoveAll(c => c.ID == mq.MessageID);
                    string failUrl = string.Format("{0}/api/zcooapi/UpdateMessageStatus?messageID={1}&messageStatusID={2}&remark={3}&isIncludeSender={4}", apiBaseUrl,mq.MessageID,3,e.Message,true);
                    var result = _httper.Get(failUrl);
                    if (result != "OK")
                    {
                        _httper.Get(failUrl);
                    }
                    sendWXTemplateMessageResponseModel = new SendWXTemplateMessageResponseModel ();
                    sendWXTemplateMessageResponseModel.ErrCode = -1000;
                    return sendWXTemplateMessageResponseModel;
                }
                return sendWXTemplateMessageResponseModel;
            }
        }

        /// <summary>
        /// 设置模板消息发送成功
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="serverID"></param>
        /// <param name="mq"></param>
        /// <param name="sendWXTemplateMessageResponseModel"></param>
        /// <param name="wxPublicAccountBlockStatuses"></param>
        /// <returns></returns>
        private static CommonResponseModel SetTemplateMessageSuccess(string apiBaseUrl, int serverID,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq,
            SendWXTemplateMessageResponseModel sendWXTemplateMessageResponseModel
            )
        {
            //更新消息队列状态
            var dataForUpdateMessageQueueStatusToSent = string.Format("serverID={0}&messageQueueIDs={1}&AppMsgIDs={2}", serverID, mq.ID, sendWXTemplateMessageResponseModel.MsgID);
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
        private static CommonResponseModel SetTemplateMessageInvalidPushNo(
            string apiBaseUrl,
            Proc_GetRobotServerMessageQueueForWXRobot_Result mq,
            SendWXTemplateMessageResponseModel sendWXTemplateMessageResponseModel
            )
        {

            var urlForUpdateMessageQueueToInvalidStatus = string.Format("{0}/api/zcooApi/UpdateMessageQueueToInvalidStatus", apiBaseUrl);
            string dataForUpdateMessageQueueToInvalidStatus = Common.HTTP.PostDataHandler.GetPostData
                (
                    new List<PostDataRequestModel>() 
                                            {
                                                   new PostDataRequestModel(){ParameterName = "MessageQueueID",ParameterValue = mq.ID,ConvertToHtml = false},
                                                   new PostDataRequestModel(){ParameterName = "InvalidReason",ParameterValue = Newtonsoft.Json.JsonConvert.SerializeObject(sendWXTemplateMessageResponseModel),ConvertToHtml =false}
                                            }
                );

            var updateMessageQueueToInvalidStatusResultString = _httper.Post(urlForUpdateMessageQueueToInvalidStatus, dataForUpdateMessageQueueToInvalidStatus);

            var updateMessageQueueToInvalidStatusResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updateMessageQueueToInvalidStatusResultString);

            return updateMessageQueueToInvalidStatusResponseModel;
        }

        /// <summary>
        /// 清除模版消息字符串中的图片相关Html标签，并用换行替代，最后将处理后的字符串返回
        /// </summary>
        /// <param name="data">要处理的字符串数据</param>
        /// <returns>返回处理后的字符串</returns>
        private static string ClearImageHtml(string data)
        {
            if (data.Contains("\\\\\\"))
            {
                data = data.Replace("\\\\\\", "");
            }
            Regex regex = new Regex("<br><div class=\"thumbnail\">.*?<br>");
            bool isMatch = regex.IsMatch(data);
            if (isMatch)
            {
                var match = regex.Match(data);
                if (!string.IsNullOrEmpty(match.Value))
                {
                    data = data.Replace(match.Value, "\\n");
                    while (match.NextMatch().Success)
                    {
                        string s = match.NextMatch().Value;
                        data = data.Replace(s, "\\n");
                        match = match.NextMatch();
                    }
                }
            }
            return data;
        }

        /// <summary>
        /// accessToken过期时，更新模板消息api
        /// </summary>
        /// <param name="publicAccountID">公众号</param>
        private static void UpdateTemplateMessageBaseUrl(string apiBaseUrl, int publicAccountID)
        {
            string url = string.Format("{0}/api/zcooApi/UpdateTemplateMessageBaseUrl", apiBaseUrl);
            string data = string.Format("publicAccountID={0}", publicAccountID);

            for (int i = 0; i < 3; i++)
            {
                var result = Httper.Post(url, data);
                if (result == "OK")
                {
                    break;
                }
            }
        }
    }
}
