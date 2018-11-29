using Models;
using Models.Commons;
using Models.Https;
using Models.PublicAccounts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WXRobot
{
    public partial class Program
    {
        /// <summary>
        /// 同步公众账号Block模型。
        /// </summary>
        /// <param name="wxPublicAccountBlockStatusModel"></param>
        /// <param name="messageQueues"></param>
        /// <param name="refresh">是否重新刷新WXPublicAccountBlockStatusModel。如果是：把WXPublicAccountBlockStatusModel下所有的IsBlocked改回为false。</param>
        /// <param name="autoRefreshAfter15">超过15秒的，是否自动把对应的公众账号修改为IsBlocked = false。</param>
        /// <returns></returns>
        private static IEnumerable<WXPublicAccountBlockStatusModel> SyncWXPublicAccountBlockStatusModel(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatusModel, IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues, bool refresh, bool autoRefreshAfter15, double checkWXIntervalWhenBlock)
        {
            var wxPublicAccountBlockStatusModelList = wxPublicAccountBlockStatusModel.ToList();
            var messageQueuesDistinctList = Common.Lists.ListHandler.DistinctBy(messageQueues, m => m.PublicAccountID).ToList();

            #region 如果公众Block模型中，在消息队列不存在，要删除
            Proc_GetRobotServerMessageQueueForWXRobot_Result theMessageQueue = null;
            WXPublicAccountBlockStatusModel[] wxPublicAccountBlockStatusModelArray = new WXPublicAccountBlockStatusModel[wxPublicAccountBlockStatusModelList.Count];

            wxPublicAccountBlockStatusModelList.CopyTo(wxPublicAccountBlockStatusModelArray);

            foreach (var pba in wxPublicAccountBlockStatusModelArray)
            {
                theMessageQueue = messageQueuesDistinctList.FirstOrDefault(mqd => mqd.PublicAccountID == pba.PublicAccountID);

                if (theMessageQueue == null)
                {
                    wxPublicAccountBlockStatusModelList.Remove(pba);
                }
            }
            #endregion

            #region 确保公众账号中，加入了消息队列中，新进的公众账号，也要记录
            WXPublicAccountBlockStatusModel thePublicAccount = null;
            Proc_GetRobotServerMessageQueueForWXRobot_Result[] messageQueuesArray = new Proc_GetRobotServerMessageQueueForWXRobot_Result[messageQueuesDistinctList.Count];

            messageQueuesDistinctList.CopyTo(messageQueuesArray);

            foreach (var mqd in messageQueuesArray)
            {
                thePublicAccount = wxPublicAccountBlockStatusModelList.FirstOrDefault(p => p.PublicAccountID == mqd.PublicAccountID);

                if (thePublicAccount == null)
                {
                    wxPublicAccountBlockStatusModelList.Add(new WXPublicAccountBlockStatusModel() { PublicAccountID = (int)mqd.PublicAccountID, IsBlocked = false, BlockedDateTime = DateTime.MinValue });
                }
            }
            #endregion

            if (refresh)
            {
                wxPublicAccountBlockStatusModelList = wxPublicAccountBlockStatusModelList.Select(c =>
                {
                    c.IsBlocked = false;
                    return c;
                }).ToList();
            }
            else if (autoRefreshAfter15) //如果没有强制指定要刷新，但那些超过15秒的公众账号，也应该自动刷新为IsBlocked = false
            {
                DateTime currentDT = Common.Time.NowHandler.GetNowByTimeZone();

                wxPublicAccountBlockStatusModelList = wxPublicAccountBlockStatusModelList.Select(p =>
                {
                    if (p.BlockedDateTime != DateTime.MinValue &&
                        p.BlockedDateTime < currentDT.AddSeconds(-checkWXIntervalWhenBlock))
                    {
                        p.IsBlocked = false;
                    }

                    return p;
                }).ToList();
            }

            return wxPublicAccountBlockStatusModelList;
        }

        /// <summary>
        /// 刷新微信公众账号Block模型。刷新意味关重新把所有公众账号中的IsBlocked，修改为false。
        /// 以便WXRobot（微信机器人），可以重新尝试这些公众账号。
        /// 刷新时，会考虑消息队列新加入的，同时也加入到微信公众账号Block模型中。
        /// </summary>
        /// <param name="wxPublicAccountBlockStatusModel"></param>
        /// <param name="messageQueues"></param>
        /// <returns></returns>
        private static IEnumerable<WXPublicAccountBlockStatusModel> RefreshWXPublicAccountBlockStatusModel(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatusModel, IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues)
        {
            var wxPublicAccountBlockStatusModelList = wxPublicAccountBlockStatusModel.ToList();
            var messageQueuesDistinctList = Common.Lists.ListHandler.DistinctBy(messageQueues, m => m.PublicAccountID).ToList();
            WXPublicAccountBlockStatusModel thePublicAccount = null;

            Proc_GetRobotServerMessageQueueForWXRobot_Result[] messageQueuesArray = new Proc_GetRobotServerMessageQueueForWXRobot_Result[messageQueuesDistinctList.Count];

            messageQueuesDistinctList.CopyTo(messageQueuesArray);

            //不存在的公众账号，加入到模型中
            foreach (var mqa in messageQueuesArray)
            {
                thePublicAccount = wxPublicAccountBlockStatusModel.FirstOrDefault(p => p.PublicAccountID == mqa.PublicAccountID);

                if (thePublicAccount == null)
                {
                    wxPublicAccountBlockStatusModelList.Add(new WXPublicAccountBlockStatusModel() { PublicAccountID = (int)mqa.PublicAccountID, BlockedDateTime = DateTime.MinValue, IsBlocked = false });
                }
            }

            wxPublicAccountBlockStatusModel = wxPublicAccountBlockStatusModel.Select(c =>
            {
                c.IsBlocked = false;
                return c;
            }).AsEnumerable();

            return wxPublicAccountBlockStatusModel;
        }

        /// <summary>
        /// 通过消息队列(messageQueues)，生成公众账号Bocked状态模型。【即以PublicAccountID为唯一，创建所有此消息队列下的公众账号模型】
        /// </summary>
        /// <param name="wxPublicAccountBlockStatusModel"></param>
        /// <param name="messageQueues"></param>
        /// <returns></returns>
        private static IEnumerable<WXPublicAccountBlockStatusModel> BuildWXPublicAccountBlockStatusModel(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatusModel, IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues)
        {
            //以公众账号为唯一键，生成新列
            var messageQueuesDistinctList = Common.Lists.ListHandler.DistinctBy(messageQueues, m => m.PublicAccountID).ToList();
            var wxPublicAccountBlockStatusModelList = wxPublicAccountBlockStatusModel.ToList();

            //把每个唯一的公众账号ID加入列表
            messageQueuesDistinctList.ForEach(m =>
            {
                //判断公众账号Blocked模型里，是否有此项
                var wxPublicAccountBlockStatusModelExisting = wxPublicAccountBlockStatusModel.FirstOrDefault(p => p.PublicAccountID == m.PublicAccountID);
                if (wxPublicAccountBlockStatusModelExisting == null) //存在里，添加
                {
                    wxPublicAccountBlockStatusModelList.Add(new WXPublicAccountBlockStatusModel() { PublicAccountID = (int)m.PublicAccountID, IsBlocked = false, BlockedDateTime = DateTime.MinValue });
                }
            });

            return wxPublicAccountBlockStatusModelList;
        }

        /// <summary>
        /// 判断是否有公众账号，还是Unblock状态，有的话，返回对应的Unblock公众账号ID。
        /// 如果没有的话，直接返回0。
        /// 此方法会自动删除公众账号Block模型中，消息队列中不存在的公众账号。
        /// </summary>
        /// <param name="wxPublicAccountBlockStatusModel"></param>
        /// <param name="messageQueues"></param>
        /// <returns></returns>
        private static int HasWXPublicAccountUnblock(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatusModel, IEnumerable<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues)
        {
            var wxPublicAccountBlockStatusModelList = wxPublicAccountBlockStatusModel.ToList();
            //var messageQueuesDistinctList = Common.Lists.ListHandler.DistinctBy(messageQueues, mq => mq.PublicAccountID);
            //Proc_GetRobotServerMessageQueueForWXRobot_Result theMessageQueue = null;

            ////把消息队列中，不存在的公众账号，从公众账号Block模型中删除
            //WXPublicAccountBlockStatusModel[] wxPublicAccountBlockStatusModelArray = new WXPublicAccountBlockStatusModel[wxPublicAccountBlockStatusModel.ToList().Count];

            //wxPublicAccountBlockStatusModel.ToList().CopyTo(wxPublicAccountBlockStatusModelArray);
            //foreach (var pbs in wxPublicAccountBlockStatusModelArray)
            //{
            //    theMessageQueue = messageQueues.FirstOrDefault(mq => mq.PublicAccountID == pbs.PublicAccountID);

            //    if (theMessageQueue == null) //如果为空，删除
            //    {
            //        wxPublicAccountBlockStatusModelList.Remove(pbs);
            //    }
            //}

            var publicAccountID = 0;
            var wxPublicAccountUnblocked = wxPublicAccountBlockStatusModelList.FirstOrDefault(p => p.IsBlocked == false);

            if (wxPublicAccountUnblocked != null)
            {
                publicAccountID = wxPublicAccountUnblocked.PublicAccountID;
            }

            return publicAccountID;
        }

        /// <summary>
        /// 此公众账号目前是否正在Block状态。
        /// </summary>
        /// <param name="wxPublicAccountBlockStatusModel">所有的公众账号列表</param>
        /// <param name="publicAccountID">要检查的公众账号</param>
        /// <returns></returns>
        //private static bool IsPublicAccountBlocked(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatusModel, int publicAccountID)
        //{
        //    var thePublicAccount = wxPublicAccountBlockStatusModel.FirstOrDefault(p => p.PublicAccountID == publicAccountID);

        //    var isBlocked = thePublicAccount.IsBlocked;

        //    return isBlocked;
        //}

        /// <summary>
        /// 判断是否应该尝试发“微信预览信息”。
        /// 条件：1、IsBlocked == false。 2、BlockedDateTime  > checkWXIntervalWhenBlock秒。
        /// </summary>
        /// <param name="wxPublicAccountBlockStatusModel"></param>
        /// <param name="publicAccountID"></param>
        /// <param name="checkWXIntervalWhenBlock">大于这个时间间隔（单位：秒），才会视为尝试。</param>
        /// <returns></returns>
        private static bool ShouldTrySendPreviewMessage(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatusModel, int publicAccountID, int checkWXIntervalWhenBlock)
        {
            var shouldTry = false;
            var currentDateTime = Common.Time.NowHandler.GetNowByTimeZone();//DateTime.Now;

            var thePublicAccount = wxPublicAccountBlockStatusModel.FirstOrDefault(p => p.PublicAccountID == publicAccountID);

            if (!thePublicAccount.IsBlocked) //如果非Block状态，就应该试发送
            {
                shouldTry = true;
            }
            else //如果是Block的，也不要紧，只要时间是大于checkWXIntervalWhenBlock秒，就应该尝试
            {
                DateTime currentDT = Common.Time.NowHandler.GetNowByTimeZone();
                DateTime startDT = currentDT.AddSeconds(-checkWXIntervalWhenBlock);

                if (thePublicAccount.BlockedDateTime < startDT)
                {
                    shouldTry = true;
                }
            }

            return shouldTry;
        }

        /// <summary>
        /// 把当前公众账号中的模型修改为Blocked状态。【注：此方法只修改模型，不改数据库的】
        /// </summary>
        /// <param name="wxPublicAccountBlockStatuses"></param>
        /// <param name="publicAccountID"></param>
        /// <returns></returns>
        private static IEnumerable<WXPublicAccountBlockStatusModel> UpdateWXPublicAccountBlockStatusModelToBlockStatus(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatuses, int publicAccountID)
        {
            var wxPublicAccountBlockStatus = wxPublicAccountBlockStatuses.FirstOrDefault(p => p.PublicAccountID == publicAccountID);

            wxPublicAccountBlockStatus.IsBlocked = true;
            wxPublicAccountBlockStatus.BlockedDateTime = Common.Time.NowHandler.GetNowByTimeZone();

            return wxPublicAccountBlockStatuses;
        }

        /// <summary>
        /// 把当前公众账号中的模型修改为Unblocked状态。【注：此方法只修改模型，不改数据库的】
        /// </summary>
        /// <param name="wxPublicAccountBlockStatuses"></param>
        /// <param name="publicAccountID"></param>
        /// <returns></returns>
        private static IEnumerable<WXPublicAccountBlockStatusModel> UpdateWXPublicAccountBlockStatusModelToUnblockStatus(IEnumerable<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatuses, int publicAccountID)
        {
            var wxPublicAccountBlockStatus = wxPublicAccountBlockStatuses.FirstOrDefault(p => p.PublicAccountID == publicAccountID);

            if (wxPublicAccountBlockStatus != null)
            {
                wxPublicAccountBlockStatus.IsBlocked = false;
                wxPublicAccountBlockStatus.BlockedDateTime = DateTime.MinValue;
            }

            return wxPublicAccountBlockStatuses;
        }

        /// <summary>
        /// 修改数据库中公众账号为Blocked状态。
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="publicAccountID"></param>
        /// <returns></returns>
        private static CommonResponseModel UpdatePublicAccountToBlockStatus(string apiBaseUrl, int publicAccountID)
        {
            var urlForUpdatePublicAccountToBlockStatus = string.Format("{0}/api/wx/UpdatePublicAccountToBlockedStatus", apiBaseUrl);  //fuxily
            string dataForUpdatePublicAccountToBlockStatus = Common.HTTP.PostDataHandler.GetPostData
                (
                    new List<PostDataRequestModel>() 
                                            {
                                                   new PostDataRequestModel(){ParameterName = "PublicAccountID",ParameterValue = publicAccountID,ConvertToHtml = false}
                                            }
                );

            var updatePublicAccountToBlockStatusResultString = _httper.Post(urlForUpdatePublicAccountToBlockStatus, dataForUpdatePublicAccountToBlockStatus);

            var updatePublicAccountToBlockStatusResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updatePublicAccountToBlockStatusResultString);

            return updatePublicAccountToBlockStatusResponseModel;
        }

        /// <summary>
        /// 修改数据库中公众账号为Unblocked状态。
        /// </summary>
        /// <param name="apiBaseUrl"></param>
        /// <param name="publicAccountID"></param>
        /// <returns></returns>
        private static CommonResponseModel UpdatePublicAccountToUnblockedStatus(string apiBaseUrl, int publicAccountID)
        {
            var urlForUpdatePublicAccountToUnblockedStatus = string.Format("{0}/api/wx/UpdatePublicAccountToUnblockedStatus", apiBaseUrl);  //fuxily
            string dataForUpdatePublicAccountToUnblockedStatus = Common.HTTP.PostDataHandler.GetPostData
                (
                    new List<PostDataRequestModel>() 
                                            {
                                                   new PostDataRequestModel(){ParameterName = "PublicAccountID",ParameterValue = publicAccountID,ConvertToHtml = false}
                                            }
                );

            var updatePublicAccountToUnblockedStatusResultString = _httper.Post(urlForUpdatePublicAccountToUnblockedStatus, dataForUpdatePublicAccountToUnblockedStatus);

            var updatePublicAccountToUnblockedStatusResponseModel = Newtonsoft.Json.JsonConvert.DeserializeObject<CommonResponseModel>(updatePublicAccountToUnblockedStatusResultString);

            return updatePublicAccountToUnblockedStatusResponseModel;
        }
    }
}
