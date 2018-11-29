using Models;
using Models.Commons;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WXRobot
{
    public partial class Program
    {
        /// <summary>
        /// 把消息队列对应的Widget状态，修改为“已删除”。
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="messageQueueID"></param>
        /// <returns></returns>
        private static CommonResponseModel UpdateMessageQueueWidgetToDeleteStatus(string apiBaseUrl, long messageQueueID)
        {
            #region 标记对应的消息列队中的Widget状态为true，说明消息列队在微信公众平台中，对应的AppMsgID块，已经被删除了
            var urlForUpdateMessageQueueWidgetToDeleteStatus = string.Format("{0}/api/zcooApi/UpdateMessageQueueWidgetToDeleteStatus?messageQueueID={1}", apiBaseUrl, messageQueueID);
            var urlForUpdateMessageQueueWidgetToDeleteStatusString = _httper.Post(urlForUpdateMessageQueueWidgetToDeleteStatus, null);
            var urlForUpdateMessageQueueWidgetToDeleteStatusResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(urlForUpdateMessageQueueWidgetToDeleteStatusString);
            #endregion

            return urlForUpdateMessageQueueWidgetToDeleteStatusResponseModel;
        }

        private static IEnumerable<Proc_GetAppMsgIDsWidgetIsNotDeletedByServerID_Result> GetAppMsgIDsWidgetIsNotDeletedByServerID(string apiBaseUrl, int serverID)
        {
            #region 标记对应的消息列队中的Widget状态为true，说明消息列队在微信公众平台中，对应的AppMsgID块，已经被删除了
            var getAppMsgIDsWidgetIsNotDeletedByServerIDString = _httper.Get(string.Format("{0}/api/ZcooApi/GetAppMsgIDsWidgetIsNotDeletedByServerID?serverID={1}", apiBaseUrl, serverID));

            //把取到的消息队列字符串，转换为json
            var getAppMsgIDsWidgetIsNotDeletedByServerIDResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<Proc_GetAppMsgIDsWidgetIsNotDeletedByServerID_Result>>(getAppMsgIDsWidgetIsNotDeletedByServerIDString);
            #endregion

            return getAppMsgIDsWidgetIsNotDeletedByServerIDResponseModel;
        }

        /// <summary>
        /// 直接取消息队列。此方法，可以取全部或者只取Unblocked的消息队列。
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="serverID"></param>
        /// <param name="messageValidPeriodDiff"></param>
        /// <param name="onlyUnblocked">是否只取Unblocked状态公众账号下的消息队列。</param>
        /// <returns></returns>
        public static IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> GetMessageQueues(string apiBaseUrl, int serverID, int messageValidPeriodDiff, int onlyUnblocked, int? messagePushTypeID)
        {
            var messageQueuesString = _httper.Get(string.Format("{0}/api/ZcooApi/GetRobotServerMessageQueueForWXRobot?serverID={1}&messageValidPeriodDiff={2}&onlyUnblocked={3}&messagePushTypeID={4}", apiBaseUrl, serverID, messageValidPeriodDiff, onlyUnblocked, messagePushTypeID));

            //把取到的消息队列字符串，转换为json
            var messageQueues = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result>>(messageQueuesString).ToList();

            return messageQueues;
        }

        /// <summary>
        /// 把消息队列中，所有是此公众账号(pubicAccountID参数指定)，都修改BlockedDateTime为当前时间。【用于进一步排序用，按机率考虑，原来的，下次执行还可能是Blocked状态】
        /// </summary>
        /// <param name="messageQueues"></param>
        /// <param name="publicAccountID"></param>
        /// <param name="isBlocked"></param>
        /// <returns></returns>
        public static IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> UpdateMessageQueuesBlockDateTime(IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues, int publicAccountID, bool setToBlockOrNot)
        {
            var messageQueuesList = messageQueues.ToList();
            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();

            messageQueues = messageQueuesList.Select(mq =>
            {
                if (mq.PublicAccountID == publicAccountID)
                {
                    if (setToBlockOrNot)
                    { //如果是Block，设置为当前时间
                        mq.BlockedDateTime = currentDateTime;
                    }
                    else
                    {
                        mq.BlockedDateTime = DateTime.MinValue;
                    }
                }

                return mq;
            });

            return messageQueues;
        }

        /// <summary>
        /// 验证正在处理中的消息队列，是否有尝试发送预览消息的。
        /// 条件：当BlockedDateTime + checkWXIntervalWhenBlock（当出现AntiSpam时暂时间隔） 小于 当前时间
        /// </summary>
        /// <param name="messageQueues"></param>
        /// <param name="checkWXIntervalWhenBlock">当被微信限制时，多少秒重试一次。默认：15秒。</param>
        /// <returns></returns>
        private static bool HasShouldTryMessageQueues(IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues, int checkWXIntervalWhenBlock)
        {
            bool hasShouldTry = false;

            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();

            var theMessageQueue = messageQueues.FirstOrDefault(mq => mq.BlockedDateTime.AddSeconds(checkWXIntervalWhenBlock) < currentDateTime && mq.ShouldDelete == false);

            if (theMessageQueue != null)
            {
                hasShouldTry = true;
            }

            return hasShouldTry;
        }

        private static bool HasShouldTryPreviewMessageQueues(IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> previewMessageQueues, int checkWXIntervalWhenBlock)
        {
            bool hasShouldTry = false;

            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();

            var theMessageQueue = previewMessageQueues.FirstOrDefault(mq => mq.BlockedDateTime.AddSeconds(checkWXIntervalWhenBlock) < currentDateTime && mq.ShouldDelete == false);

            if (theMessageQueue != null)
            {
                hasShouldTry = true;
            }

            return hasShouldTry;
        }

        /// <summary>
        /// 判断当前消息队列，是否应该尝试发送预览的。
        /// </summary>
        /// <param name="messageQueue"></param>
        /// <returns></returns>
        private static bool IsShouldTryMessageQueue(Proc_GetRobotServerMessageQueueForWXRobot_Result messageQueue, int checkWXIntervalWhenBlock)
        {
            var shouldTry = false;
            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();

            if (messageQueue.BlockedDateTime.AddSeconds(checkWXIntervalWhenBlock) < currentDateTime && messageQueue.ShouldDelete == false) //如果当前时间大于下次尝试时间
            {
                shouldTry = true;
            }

            return shouldTry;
        }

        /// <summary>
        /// 当模板消息发送达到上限时，设置此公众号下的所有消息队列的下次发送时间
        /// </summary>
        /// <param name="messageQueues"></param>
        /// <param name="publicAccountID"></param>
        /// <returns></returns>
        private static IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> SetTemplateMessageQueueNextSendDateTime(IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues, int? publicAccountID)
        {
            var messageQueuesList = messageQueues.ToList();
            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();
            var nextSendDataTime = Convert.ToDateTime(currentDateTime.AddDays(1).ToShortDateString());

            messageQueues = messageQueuesList.Select(mq =>
            {
                if (mq.PublicAccountID == publicAccountID)
                {
                    mq.NextSendDateTime = nextSendDataTime;
                }

                return mq;
            });

            return messageQueues;
        }

        /// <summary>
        /// 判断当前消息队列，是否应该尝试发送模板消息。
        /// </summary>
        /// <param name="messageQueue"></param>
        /// <returns></returns>
        private static bool IsShouldTrySendMessageQueue(Proc_GetRobotServerMessageQueueForWXRobot_Result messageQueue)
        {
            var shouldTry = false;
            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();

            if (messageQueue.NextSendDateTime < currentDateTime && messageQueue.ShouldDelete == false) //如果当前时间大于下次尝试时间
            {
                shouldTry = true;
            }

            return shouldTry;
        }

        /// <summary>
        /// 验证正在处理中的消息队列，是否有尝试下次发送消息的。
        /// </summary>
        /// <param name="previewMessageQueues"></param>
        /// <param name="checkWXIntervalWhenBlock"></param>
        /// <returns></returns>
        private static bool HasShouldTryNextSendMessageQueues(IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues)
        {
            bool hasShouldTry = false;

            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();
            var theMessageQueue = messageQueues.FirstOrDefault(mq => mq.NextSendDateTime < currentDateTime && mq.ShouldDelete == false);

            if (theMessageQueue != null)
            {
                hasShouldTry = true;
            }

            return hasShouldTry;
        }

    }
}
