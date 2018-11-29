using DependencyResolver;
using Ninject;
using Ninject.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Configuration;
using Models;
using Interfaces.HTTP;
using Common.HTTP;
using Models.Commons;
using Models.WX.MessageModel.PreviewMessage;
using Models.Https;
using Models.Enum.Logs;
using Models.PublicAccounts;
using System.Linq.Dynamic;
using Microsoft.AspNet.SignalR.Client;
using System.Timers;
using Models.WXRobots.WXRobotHub;
using System.Threading.Tasks;
using System.Threading;
using Models.Enum.Messages;
using Models.WX.MessageModel.TemplateMessage;
using Biz.WX.AccessToken;
using Newtonsoft.Json;
using Models.ShortMessage;
using Models.MessageQueues;

namespace WXRobot
{
    static class Extensions
    {
        public static IList<T> Clone<T>(this IList<T> listToClone) where T : ICloneable
        {
            return listToClone.Select(item => (T)item.Clone()).ToList();
        }
    }

    public partial class Program
    {
        private static DateTime startDT;
        private static DateTime currentDT;
        private static int hours;
        private static string apiBaseUrl = string.Empty;
        private static int serverID;

        private static int deleteAppMsgIDsWidgetsStartHour;
        private static int deleteAppMsgIDsWidgetsEndHour;
        private static int checkWXIntervalWhenBlock;
        private static int retryTimes;
        private static int relogin = 0;
        private static double checkDBIntervalValue;
        private static int messageValidPeriodDiff;
        private static int tryIntervalWhenException;

        private static int timesForException;

        private static int timesForCheckServerConnection = 1;

        private static string messageString = string.Empty;
        private static string messageQueuesString = string.Empty;
        private static string urlForSendPreviewMessage = string.Empty;
        private static string dataForSendPreviewMessage = string.Empty;

        private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
        private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> previewMessageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
        private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> templateMessageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
        private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> shortMessageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();

        //private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueuesClone = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
        private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> messageQueuesForUnblocked = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
        private static List<Proc_GetRobotServerMessageQueueForWXRobot_Result> previewMessageQueuesForUnblocked = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
        private static List<WXPublicAccountBlockStatusModel> wxPublicAccountBlockStatuses = new List<WXPublicAccountBlockStatusModel>();
        private static SendWXPreviewMessageResponseModel sendWXPreviewMessageResponseModel = new SendWXPreviewMessageResponseModel();
        private static SendWXTemplateMessageResponseModel sendWXTemplateMessageResponseModel = new SendWXTemplateMessageResponseModel();
        private static Proc_GetMessageInfoByMessageIDs_Result message = new Proc_GetMessageInfoByMessageIDs_Result();
        private static List<Proc_GetMessageInfoByMessageIDs_Result> messages = new List<Proc_GetMessageInfoByMessageIDs_Result>();
        private static CommonResponseModel updatePublicAccountToBlockStatusResponseModel = new CommonResponseModel();
        private static CommonResponseModel updatePublicAccountToUnblockedStatusResponseModel = new CommonResponseModel();
        private static CommonResponseModel updateMessageQueueToInvalidStatusResponseModel = new CommonResponseModel();
        private static List<MessageQueueIDModel> messageQueueIDs = new List<MessageQueueIDModel>();
        private static int showSourceUrlInContent;
        private static int showCoverInContent;
        private static int messageCapacity;

        //SignalR代理对象
        private static HubConnection hubConnection = null;
        private static IHubProxy wxRobotHubProxy = null;

        /// <summary>
        /// 同步锁，防止多个后台推送，并行。
        /// </summary>
        private static object syncLock = new object();
        private static object sendMessageLock = new object();

        /// <summary>
        /// Timer的同步锁。用于SignalR连接断开时，锁定，给足够的时间，让他创建连接。
        /// </summary>
        private static object syncLockForStartSignalConnection = new object();

        private static object syncLockForCreateSignalRProxy = new object();

        private static object syncLockForTimer = new object();

        private static object syncLockForWXRobotHubItem = new object();

        /// <summary>
        /// 判断微信机器人，是否正在发送消息?
        /// </summary>
        private static bool isSendingPreviewMessage = false;
        private static bool isSendingTemplateMessage = false;
        private static bool isSendingShortMessage = false;

        /// <summary>
        /// 判断signal是否正在通知机器人发消息，假如是的话就不要执行10s定时发消息。
        /// </summary>
        private static bool isSignalRSendingMessages = false;

        private static int proxyCreationTimes = 0;

        private static Ninject.IKernel _kernel;

        private static IHttper _httper;
        public static IHttper Httper
        {
            get
            {
                return _httper;
            }
        }

        static void Main(string[] args)
        {
            #region Ninject注入
            InjectNinject();
            InitialiateObject();
            #endregion

            #region 提示信息
            Console.WriteLine("正在启动“微信机器人”...");
            Common.Time.WaitFor.wait(new TimeSpan(0, 0, 2));
            Console.WriteLine("“微信机器人”，初始化成功！");
            Common.Time.WaitFor.wait(new TimeSpan(0, 0, 2));
            //服务启动的时间
            startDT = Common.Time.NowHandler.GetNowByTimeZone();//DateTime.Now;
            DateTime nextCheckDBDT = Common.Time.NowHandler.GetNowByTimeZone();//DateTime.Now; //下一次检查数据库的时间
            currentDT = nextCheckDBDT.AddSeconds(1);
            hours = currentDT.Hour;
            timesForException = 1;
            #endregion

            #region 得到配置信息
            serverID = Convert.ToInt16(WebConfigurationManager.AppSettings["ServerID"].ToString());

            //得到检查数据库的间隔（默认：3000毫秒）
            string checkDBIntervalString = WebConfigurationManager.AppSettings["CheckDBInterval"].ToString();
            double.TryParse(checkDBIntervalString, out checkDBIntervalValue);

            //得到"消息有效期差"
            string messageValidPeriodDiffString = WebConfigurationManager.AppSettings["MessageValidPeriodDiff"].ToString();
            int.TryParse(messageValidPeriodDiffString, out messageValidPeriodDiff);

            //得到API基本Url
            apiBaseUrl = WebConfigurationManager.AppSettings["APIBaseUrl"].ToString();

            //得到消息容量
            string nessageCapacityString = WebConfigurationManager.AppSettings["MessageCapacity"].ToString();
            int.TryParse(nessageCapacityString, out messageCapacity);

            //当被微信限制时，多少秒后重试。默认：15秒。
            string checkWXIntervalWhenBlockString = WebConfigurationManager.AppSettings["CheckWXIntervalWhenBlock"].ToString();
            int.TryParse(checkWXIntervalWhenBlockString, out checkWXIntervalWhenBlock);

            //当发生异常时，系统等待的时间间隔【默认：15秒】
            string tryIntervalWhenExceptionString = WebConfigurationManager.AppSettings["TryIntervalWhenException"].ToString();
            int.TryParse(tryIntervalWhenExceptionString, out tryIntervalWhenException);

            //重试次数
            string retryTimesString = WebConfigurationManager.AppSettings["RetryTimes"].ToString();
            int.TryParse(retryTimesString, out retryTimes);

            #region 清除豆腐块时间段
            //开始小时。默认：0时
            string deleteAppMsgIDsWidgetsStartHourString = WebConfigurationManager.AppSettings["DeleteAppMsgIDsWidgetsStartHour"].ToString();
            int.TryParse(deleteAppMsgIDsWidgetsStartHourString, out deleteAppMsgIDsWidgetsStartHour);

            //结束小时。默认：6时
            string deleteAppMsgIDsWidgetsEndHourString = WebConfigurationManager.AppSettings["DeleteAppMsgIDsWidgetsEndHour"].ToString();
            int.TryParse(deleteAppMsgIDsWidgetsEndHourString, out deleteAppMsgIDsWidgetsEndHour);
            #endregion

            #region 封面和阅读原文配置
            string showCoverInContentString = WebConfigurationManager.AppSettings["ShowCoverInContent"].ToString();
            int.TryParse(showCoverInContentString, out showCoverInContent);

            string showSourceUrlInContentString = WebConfigurationManager.AppSettings["ShowSourceUrlInContent"].ToString();
            int.TryParse(showSourceUrlInContentString, out showSourceUrlInContent);
            #endregion
            #endregion

            #region 创建SignalR链接，接收服务器的推送
            CreateSignalRProxy(true);

            while (true)
            {
                Common.Time.WaitFor.wait(new TimeSpan(0, 0, 1));
                UpdateScreenInfoByVoid();
            }
            #endregion

        }

        /// <summary>
        /// 创建SignalR代理。
        /// </summary>
        /// <param name="sendPreviewMessage">创建完后，是否马上发送预览消息。</param>
        private static void CreateSignalRProxy(bool sendPreviewMessage = true)
        {
            lock (syncLockForCreateSignalRProxy)
            {
                var ctsForDispose = new CancellationTokenSource();

                var taskToDispose = Task.Factory.StartNew(() =>
                {
                    if (hubConnection != null)
                    {
                        hubConnection.Dispose();
                    }
                });

                if (!taskToDispose.Wait(3000, ctsForDispose.Token))
                {
                    ctsForDispose.Cancel();
                }

                hubConnection = new HubConnection(apiBaseUrl);
                wxRobotHubProxy = hubConnection.CreateHubProxy("WXRobotHub");

                wxRobotHubProxy.On("SendPreviewMessages", (c) =>
                {
                    isSignalRSendingMessages = true;
                    if (!isSendingPreviewMessage)
                    {
                        int messagePushTypeID =Convert.ToInt32(c);
                        isSendingPreviewMessage = true;
                        SendMessages(messagePushTypeID);
                        isSendingPreviewMessage = false;
                    }
                });


                //判断取消token，防止锁死，有时候，会意外，导致此问题，锁死，发不出信息
                var cts = new CancellationTokenSource();

                if (!hubConnection.Start().Wait(6000, cts.Token))
                {
                    cts.Cancel();
                }

                if (sendPreviewMessage && !isSendingPreviewMessage && !isSignalRSendingMessages)
                {
                    SendMessages();
                }

                proxyCreationTimes++;
            }
        }

        /// <summary>
        /// 通过客户端的ConnectionID得到对应的WXRobotHubItem。用户于判断是否服务器跟客户端连接正常。
        /// </summary>
        /// <param name="connectionID"></param>
        /// <returns></returns>
        private static WXRobotHubItem GetWXRobotHubItem(string connectionID)
        {
            WXRobotHubItem wxRobotHubItem = null;

            try
            {
                if (wxRobotHubProxy != null)
                {
                    var tokenSource = new CancellationTokenSource();
                    var token = tokenSource.Token;
                    wxRobotHubItem = wxRobotHubProxy.Invoke<WXRobotHubItem>("GetWXRobotHubItem", connectionID, serverID, messageValidPeriodDiff).Result;
                }
            }
            catch
            {
                wxRobotHubItem = null;
            }

            return wxRobotHubItem;
        }

        private static void InjectNinject()
        {
            _kernel = new StandardKernel();

            var modules = new List<INinjectModule>
				        {
					        new CommonNinjectModule(),
					        new BizNinjectModule(),
                            new ModelNinjectModule()
				        };

            _kernel.Load(modules);

            _kernel.Bind<IHttper>().To<Httper>();
        }

        private static void InitialiateObject()
        {
            _httper = _kernel.Get<Httper>();
        }

        /// <summary>
        /// 返回当前时间。
        /// </summary>
        /// <param name="startDT"></param>
        /// <param name="noException"></param>
        /// <param name="timesForException"></param>
        /// <returns></returns>
        private static DateTime UpdateScreenInfo(DateTime startDT, bool noException, int timesForException)
        {
            //int days, int hours, int minutes, int seconds,
            var currentDT = Common.Time.NowHandler.GetNowByTimeZone();//DateTime.Now;

            var DTDiff = (currentDT - startDT);
            var days = DTDiff.Days;
            var hours = DTDiff.Hours;
            var minutes = DTDiff.Minutes;
            var seconds = DTDiff.Seconds;

            //清除之前的记录
            Console.Clear();
            Console.WriteLine("正在启动“微信机械人”...");
            Console.WriteLine("“微信机械人”，初始化成功！");
            Console.WriteLine("正在读取配置信息...");
            Console.WriteLine("成功读取配置信息！");
            if (noException)
            {
                Console.WriteLine("服务已正常运行，" + days + "天" + hours + "时" + minutes + "分" + seconds + "秒！");
            }
            else
            {
                Console.WriteLine("服务正在异常处理中。。。。。（异常次数是第：" + timesForException + "次）。服务已经运行" + days + "天" + hours + "时" + minutes + "分" + seconds + "秒！");
            }

            Console.WriteLine("请输入q，退出此服务！");

            return currentDT;
        }

        #region SignalR

        private static void UpdateScreenInfoByVoid()
        {
            try
            {
                #region 每隔10次，检查一下服务器是否跟客户端仍然连接正常
                //记录第几次
                lock (syncLockForWXRobotHubItem)
                {
                    currentDT = UpdateScreenInfo(startDT, true, timesForException);
                    hours = currentDT.Hour;

                    timesForCheckServerConnection++;

                    if (hubConnection != null && (timesForCheckServerConnection % 10) == 0)
                    {
                        if (proxyCreationTimes > 0 && (isSendingPreviewMessage || isSendingTemplateMessage || isSendingShortMessage))
                        {
                            return;
                        }

                        timesForCheckServerConnection = 0;

                        if (hubConnection.ConnectionId == null)
                        {
                            hubConnection.ConnectionId = "0";
                        }

                        if (hubConnection.State != ConnectionState.Connected)
                        {
                            CreateSignalRProxy(true);
                        }
                        else
                        {
                            var theWXRobotHubItem = GetWXRobotHubItem(hubConnection.ConnectionId);

                            if (theWXRobotHubItem == null || theWXRobotHubItem.ConnectionId != hubConnection.ConnectionId) //如果返回为空，或者得到的connectionID前后不一致，说明连接断过。服务器无法跟客户端通话了。
                            {
                                //重新创建代理
                                CreateSignalRProxy(true);
                            }
                            else
                            {
                                //若内存中设置的消息下次发送时间已到达，则发送
                                Task sendTask = new Task(() => MesssageQuquesNextSentTimeArrived());
                                sendTask.Start();
                            }
                        }
                        var httper = _httper;
                        //每10秒去通知服务器去上传那些失败的图片到又拍云。
                        Task tu = new Task(() =>
                        {
                            httper.Get(string.Format("{0}/api/zcooapi/UploadFailFileToUpYun", apiBaseUrl));
                        });
                        tu.Start();
                    }
                }
                #endregion

                #region 当没有新的消息队列时，并且时间段在非繁忙时间，我们考虑清除在微信方的豆腐块
                if (hours >= deleteAppMsgIDsWidgetsStartHour && hours < deleteAppMsgIDsWidgetsEndHour)
                {
                    #region 当消息队列没有消息时，才执行
                    lock (syncLockForTimer)
                    {
                        if (previewMessageQueues.Count == 0)
                        {
                            var pendingWidgets = GetAppMsgIDsWidgetIsNotDeletedByServerID(apiBaseUrl, serverID).ToList();

                            #region 循环，清除每个还没有删除的豆腐块
                            foreach (var pw in pendingWidgets)
                            {
                                DeleteWXPreviewMessageWidgetAndUpdateDBWidgetStatusToDelete
                                        (
                                            (int)pw.PublicAccountID,
                                            (int)pw.AppMsgID,
                                            pw.MessageQueueID,
                                            apiBaseUrl
                                        );
                            }
                            #endregion
                        }
                    }
                    #endregion
                }
                #endregion
            }
            catch (Exception ex)
            {

                LogWriter.WriteLogToDBBySuperAdmin(1, ex.StackTrace, (int)SeverityLevelType.VeryUrgent, ex.Message);

                currentDT = UpdateScreenInfo(startDT, false, timesForException);
                timesForException++;
            }
        }

        private static void SendMessages()
        {
            #region 同步锁，防止多个后台推送并行，保证线程安全
            lock (syncLock)
            {
                Task taskToPriviewMessage = new Task(() => SendPreviewMessages());
                taskToPriviewMessage.Start();
                Task taskToShortMessage = new Task(() => SendShortMessages());
                taskToShortMessage.Start();
                Task taskToTemplateMessage = new Task(() => SendTemplateMessages());
                taskToTemplateMessage.Start();
            }
            #endregion
        }
        private static void SendMessages(int messagePushTypeID)
        {
            #region 同步锁，防止多个后台推送并行，保证线程安全
            lock (syncLock)
            {
                switch (messagePushTypeID)
                {
                    case 2:
                        Task taskToPriviewMessage = new Task(() => SendPreviewMessages());
                        taskToPriviewMessage.Start();
                        break;
                    case 6:
                        Task taskToTemplateMessage = new Task(() => SendTemplateMessages());
                        taskToTemplateMessage.Start();
                        break;
                    default:
                        SendMessages();
                        break;
                }
                #region 创建子线程分别处理发送不同消息
                

                

                
                #endregion
            }
            #endregion
        }

        /// <summary>
        /// 后台推送发送预览。
        /// </summary>
        private static void SendPreviewMessages()
        {
            #region 同步锁，防止多个后台推送并行，保证线程安全
            lock (syncLock)
            {
                #region 总是循环，直接当前消息队列都被清空
                while (true)
                {
                    try
                    {
                        isSendingPreviewMessage = true;

                        #region 尝试
                        #region 取得新的消息队列
                        var newPreviewMessageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();

                        if (previewMessageQueues.Count > 0 && !HasShouldTryPreviewMessageQueues(previewMessageQueues, checkWXIntervalWhenBlock)) //如果主消息队列不为空，并且此主队列中，没有可尝试的队列
                        {
                            newPreviewMessageQueues = GetMessageQueues(apiBaseUrl, serverID, messageValidPeriodDiff, 1, 2).ToList();

                        }
                        else //其他情况下，按消自创建的自然时间取消息（不用考虑禁用时，取其他公众账号的消息）
                        {
                            newPreviewMessageQueues = GetMessageQueues(apiBaseUrl, serverID, messageValidPeriodDiff, 0, 2).ToList();
                        }

                        #region 有数据时，才执行添加新消息队列
                        if (newPreviewMessageQueues != null && newPreviewMessageQueues.Count > 0)
                        {
                            previewMessageQueues.AddRange(newPreviewMessageQueues);
                        }
                        #endregion
                        #endregion

                        #region 判断当前消息队列是否为空，为空时，才退出此while循环
                        if (previewMessageQueues.Count == 0)
                        {
                            break;
                        }
                        #endregion

                        #region 添加Unblocked的消息队列到当前队列，并且按“创建时间”，升序排序
                        //总是把Unblocked的消息队列加入当前消息队列，不用担心，尽管是Count=0也没有所谓
                        previewMessageQueues.AddRange(previewMessageQueuesForUnblocked);

                        //添加完后，把另外一个队列清空
                        previewMessageQueuesForUnblocked.RemoveRange(0, previewMessageQueuesForUnblocked.Count);

                        //每次执行次，去除重复项
                        previewMessageQueues = previewMessageQueues.DistinctBy(m => m.ID).ToList();

                        //按发送时间升级，确保越早的消息，先发送【争取时间】
                        previewMessageQueues = previewMessageQueues.AsQueryable().OrderBy(string.Format("{0},{1},{2}", "BlockedDateTime", "MessagePriorityID", "ID")).ToList();//(m => m.Created).ToList();
                        #endregion

                        #region 同步模型
                        wxPublicAccountBlockStatuses = SyncWXPublicAccountBlockStatusModel(wxPublicAccountBlockStatuses, previewMessageQueues, false, true, checkWXIntervalWhenBlock).ToList();
                        #endregion

                        #region 循环消息队列，发送每个条记录
                        foreach (var mq in previewMessageQueues)
                        {
                            currentDT = UpdateScreenAndWait(startDT, timesForException, false);

                            #region 判断此消息队列对应的公众账号，是否应该尝试发送
                            //shouldTrySendPreviewMessage = ShouldTrySendPreviewMessage(wxPublicAccountBlockStatuses, (int)mq.PublicAccountID, checkWXIntervalWhenBlock);
                            var hasShouldTryMessageQueues = HasShouldTryPreviewMessageQueues(previewMessageQueues, checkWXIntervalWhenBlock);
                            if (!hasShouldTryMessageQueues) //已经没有要尝试的消息队列时，我们取其他公众账号的消息队列
                            {
                                //只取未阻塞部分的消息队列
                                previewMessageQueuesForUnblocked = GetMessageQueues(apiBaseUrl, serverID, messageValidPeriodDiff, 1, 2).ToList();

                                //如果返回为空，可能是网络原因，重新创建新的列表，不能为Null，防止出错
                                if (previewMessageQueuesForUnblocked == null)
                                {
                                    previewMessageQueuesForUnblocked = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
                                }

                                #region 把非阻塞状态的消息队列加入消息队列中
                                if (previewMessageQueuesForUnblocked.Count > 0)
                                {
                                    previewMessageQueues.AddRange(previewMessageQueuesForUnblocked);
                                    previewMessageQueuesForUnblocked.Clear();
                                }
                                else //如果没有新的消息，等一会再操作，防止在频繁取数据库
                                {
                                    Common.Time.WaitFor.wait(new TimeSpan(0, 0, 0, 0, (int)checkDBIntervalValue));
                                }
                                #endregion

                                break;
                            }
                            #endregion

                            #region 判断当前消息，是否应该尝试的
                            if (!IsShouldTryMessageQueue(mq, checkWXIntervalWhenBlock)) //如果当前消息不应该尝试的，尝试下一条消息队列
                            {
                                continue;
                            }
                            #endregion

                            #region 得到消息内容
                            message = messages.FirstOrDefault(m => m.ID == mq.MessageID);

                            #region 此消息在内存中不存在时，到数据库中取消息
                            if (message == null)
                            {
                                messageString = _httper.Get(string.Format("{0}/api/zcooApi/GetMessageInfoByMessageIDs?messageIDs={1}", apiBaseUrl, mq.MessageID));

                                //把消息，转换为json
                                message = Newtonsoft.Json.JsonConvert.DeserializeObject<IList<Proc_GetMessageInfoByMessageIDs_Result>>(messageString).FirstOrDefault();

                                //如果不为空时，加入到新队列中
                                if (message != null)
                                {
                                    //把刚刚找到的消息，加入消息队列中
                                    messages.Add(message);

                                    if (messages.Count > messageCapacity)
                                    {
                                        messages.RemoveAt(0); //删除第一个
                                    }
                                }
                            }
                            #endregion
                            #endregion

                            #region 发送预览，一直循环，直到此消息发送成功为此【解决微信AntoSpam的限制】
                            while (true)
                            {
                                #region 每次执行前，更新屏幕时间（让机器人有心跳）
                                currentDT = UpdateScreenAndWait(startDT, timesForException, false);
                                #endregion

                                #region 当消息的发送者ID和接收者ID都相等时，不用显示回复信息
                                if (!(mq.IsCopy??false)&&(mq.ToUserID != mq.FromUserID || message.MessageFunctionID == (int)MessageFunctionType.ResetPassword) && message.MessageFunctionID != (int)MessageFunctionType.OrderNotice)
                                {
                                    showSourceUrlInContent = 1;
                                }
                                else
                                {
                                    showSourceUrlInContent = 0;
                                }
                                #endregion

                                #region 尝试发送预览消息
                                //每次发送前，都把错误码设置为-1000
                                sendWXPreviewMessageResponseModel = new SendWXPreviewMessageResponseModel() { Ret = -1000 };
                                sendWXPreviewMessageResponseModel = SendPreviewMessage(apiBaseUrl, urlForSendPreviewMessage, dataForSendPreviewMessage, message, mq, showCoverInContent, showSourceUrlInContent, relogin);
                                #endregion

                                #region 无论何时，视为非强制登录
                                relogin = 0;
                                #endregion

                                #region 判断是否Cookie过期，强制重新登录（过期返回是：-3）
                                if (sendWXPreviewMessageResponseModel != null && sendWXPreviewMessageResponseModel.Ret == -3)
                                {
                                    relogin = 1;
                                    continue;
                                }
                                #endregion

                                #region 当推送号码无效时
                                var isInvalidPushNo = IsInvalidPushNo(sendWXPreviewMessageResponseModel);
                                if (isInvalidPushNo)
                                {
                                    var isSuccessfulPreviewMessage = false;
                                    var shouldRetryTimes = retryTimes;
                                    var errorMessage = "sendWXPreviewMessageResponseModel返回null";

                                    if (sendWXPreviewMessageResponseModel != null)
                                    {
                                        errorMessage = sendWXPreviewMessageResponseModel.Msg;
                                    }

                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendWXPreviewMessage", (int)SeverityLevelType.Urgent, errorMessage);

                                    //如果是-20000或者-2时，我们重新登录，尝试
                                    if (sendWXPreviewMessageResponseModel == null)
                                    {
                                        shouldRetryTimes = 5;
                                       
                                    }
                                    if (sendWXPreviewMessageResponseModel !=null &&(
                                        sendWXPreviewMessageResponseModel.Ret == -20000 ||
                                        sendWXPreviewMessageResponseModel.Ret == -2 ||
                                        sendWXPreviewMessageResponseModel.Ret == -1))
                                    {
                                        relogin = 1;
                                    }
                                    //尝试多次再设定为无效，防止微信网络原因，导致一些信息丢失
                                    int currentTryTimes = 0;
                                    #region 循环5次直到成功为此
                                    while (currentTryTimes < shouldRetryTimes && !isSuccessfulPreviewMessage)
                                    {
                                        sendWXPreviewMessageResponseModel = SendPreviewMessage(apiBaseUrl, urlForSendPreviewMessage, dataForSendPreviewMessage, message, mq, showCoverInContent, showSourceUrlInContent, relogin);

                                        relogin = 0;
                                        //记录是否成功
                                        isSuccessfulPreviewMessage = IsPreviewMessageSentSuccessfully(sendWXPreviewMessageResponseModel);

                                        //验证是否同样错误
                                        if (!isSuccessfulPreviewMessage) //不成功，继续尝试
                                        {
                                            currentTryTimes++;
                                        }
                                        else //成功的话，退出此次循环
                                        {
                                            break;
                                        }
                                    }
                                    #endregion

                                    //重置当前重试记录
                                    currentTryTimes = 0;
                                }
                                #endregion

                                #region 服务被禁止时
                                if (sendWXPreviewMessageResponseModel != null && sendWXPreviewMessageResponseModel.Ret == -20000)
                                {
                                    //让服务强制登录微信
                                    relogin = 1;

                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendWXPreviewMessage", (int)SeverityLevelType.NoEffect, sendWXPreviewMessageResponseModel.Msg);

                                    //把当前消息队列，为Blocked时间为当前
                                    previewMessageQueues = UpdateMessageQueuesBlockDateTime(previewMessageQueues, (int)mq.PublicAccountID, true).ToList();

                                    break;
                                }
                                #endregion

                                #region 被微信限制时（即：AntiSpam)
                                else if (sendWXPreviewMessageResponseModel != null &&
                                    (sendWXPreviewMessageResponseModel.Ret == -13 || sendWXPreviewMessageResponseModel.Ret == -8)) //如果被限制，每15秒重试一次
                                {
                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendWXPreviewMessage", (int)SeverityLevelType.NoEffect, sendWXPreviewMessageResponseModel.Msg);
                                    //更新数据库为中公众账号状态为Blocked
                                    updatePublicAccountToBlockStatusResponseModel = UpdatePublicAccountToBlockStatus(apiBaseUrl, (int)mq.PublicAccountID);

                                    if (updatePublicAccountToBlockStatusResponseModel != null &&
                                        string.Compare(updatePublicAccountToBlockStatusResponseModel.ReturnMessage, "OK", StringComparison.InvariantCultureIgnoreCase) == 0)
                                    {
                                        wxPublicAccountBlockStatuses = UpdateWXPublicAccountBlockStatusModelToBlockStatus(wxPublicAccountBlockStatuses, (int)mq.PublicAccountID).ToList();
                                    }

                                    //把当前消息队列，为Blocked时间为当前
                                    previewMessageQueues = UpdateMessageQueuesBlockDateTime(previewMessageQueues, (int)mq.PublicAccountID, true).ToList();

                                    //Common.Time.WaitFor.wait(new TimeSpan(0, 0, 0, 0, (int)checkDBIntervalValue));

                                    //只有一有限制，就退出此次循环，回到父级循环，让低级循环判断是否要重新取数据
                                    break;
                                }
                                #endregion

                                #region 正常发送预览信息时
                                else if (
                                            sendWXPreviewMessageResponseModel != null &&
                                            sendWXPreviewMessageResponseModel.Ret == 0
                                        )
                                {
                                    wxPublicAccountBlockStatuses = SetPreviewMessageSuccess(apiBaseUrl, serverID, mq, sendWXPreviewMessageResponseModel, wxPublicAccountBlockStatuses).ToList();

                                    //因为发送成功，我们把此公众账号下的消息队列，都改为非block状态（改BlockDateTime = DateTime.MinValue）
                                    previewMessageQueues = UpdateMessageQueuesBlockDateTime(previewMessageQueues, (int)mq.PublicAccountID, false).ToList();

                                    break;
                                }
                                #endregion

                                #region 多次尝试后，还是无效时
                                else if (IsInvalidPushNo(sendWXPreviewMessageResponseModel)) //如果还是无效时，应该取消
                                {
                                    //标记为删除此循环队列
                                    mq.ShouldDelete = true;

                                    //设置为无效
                                    SetPreviewMessageInvalidPushNo(apiBaseUrl, mq);

                                    break;
                                }
                                #endregion

                                #region 其他原因失败时。如返回Model为空等情况
                                else
                                {
                                    if (sendWXPreviewMessageResponseModel == null)
                                    {
                                        LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendWXPreviewMessage", (int)SeverityLevelType.Normal, "sendWXPreviewMessageResponseModel返回为Null");
                                    }
                                    else
                                    {
                                        LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendWXPreviewMessage", (int)SeverityLevelType.NoEffect, sendWXPreviewMessageResponseModel.Msg);
                                    }

                                    //Common.Time.WaitFor.wait(new TimeSpan(0, 0, checkWXIntervalWhenBlock));
                                    //把当前消息队列，为Blocked时间为当前
                                    previewMessageQueues = UpdateMessageQueuesBlockDateTime(previewMessageQueues, (int)mq.PublicAccountID, true).ToList();

                                    break;
                                }
                                #endregion
                            }
                            #endregion

                        }
                        #endregion

                        #region 清除消息队列，已经发送成功的
                        if (previewMessageQueues.Count > 0)
                        {
                            Proc_GetRobotServerMessageQueueForWXRobot_Result[] messageQueuesArray = new Proc_GetRobotServerMessageQueueForWXRobot_Result[previewMessageQueues.Count];

                            previewMessageQueues.ToList().CopyTo(messageQueuesArray);

                            foreach (var a in messageQueuesArray)
                            {
                                //只删除那些被标识为“应该删除”的项
                                if (a.ShouldDelete)
                                {
                                    previewMessageQueues.Remove(a);
                                }
                            }
                        }
                        #endregion
                        #endregion
                    }
                    catch (Exception e)
                    {

                        #region 异常时处理
                        int messageFunctionID = 1;

                        if (message != null)
                        {
                            messageFunctionID = message.MessageFunctionID;
                        }

                        LogWriter.WriteLogToDBBySuperAdmin(messageFunctionID, e.StackTrace, (int)SeverityLevelType.VeryUrgent, e.Message);

                        currentDT = UpdateScreenInfo(startDT, false, timesForException);
                        timesForException++;

                        Common.Time.WaitFor.wait(new TimeSpan(0, 0, 0, 0, (int)checkDBIntervalValue));
                        #endregion

                    }
                }
                #endregion

                isSendingPreviewMessage = false;
                isSignalRSendingMessages = false;
            }
            #endregion
        }

        /// <summary>
        /// 后台推送发送模板。
        /// </summary>
        private static void SendTemplateMessages()
        {
            #region 同步锁，防止多个后台推送并行，保证线程安全
            lock (syncLock)
            {
                #region 总是循环，直接当前消息队列都被清空
                while (true)
                {
                    try
                    {
                        isSendingTemplateMessage = true;

                        #region 尝试
                        #region 取得新的消息队列
                        var newTemplateMessageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();

                        newTemplateMessageQueues = GetMessageQueues(apiBaseUrl, serverID, messageValidPeriodDiff, 0, 6).ToList();

                        #region 有数据时，才执行添加新消息队列
                        if (newTemplateMessageQueues != null && newTemplateMessageQueues.Count > 0)
                        {
                            templateMessageQueues.AddRange(newTemplateMessageQueues);
                        }
                        #endregion
                        #endregion

                        #region 每次执行次，去除重复项
                        templateMessageQueues = templateMessageQueues.DistinctBy(m => m.ID).ToList();
                        #endregion

                        #region 如果内存中没有消息队列,或者没有读取到新的消息队列并且内存中没有可尝试的消息队列，跳出while循环
                        if (templateMessageQueues.Count == 0 || newTemplateMessageQueues.Count == 0 && (templateMessageQueues.Count > 0 && !HasShouldTryNextSendMessageQueues(templateMessageQueues)))
                        {
                            break;
                        }
                        #endregion

                        #region 按发送时间升级，确保越早的消息，先发送【争取时间】
                        templateMessageQueues = templateMessageQueues.AsQueryable().OrderBy(string.Format("{0},{1}", "MessagePriorityID", "ID")).ToList();//(m => m.Created).ToList(); 
                        #endregion

                        #region 循环消息队列，发送每个条记录
                        foreach (var mq in templateMessageQueues)
                        {
                            currentDT = UpdateScreenAndWait(startDT, timesForException, false);

                            #region 判断当前消息，是否应该尝试的
                            if (!IsShouldTrySendMessageQueue(mq))
                            {
                                continue;
                            }
                            #endregion

                            #region 得到消息内容
                            message = messages.FirstOrDefault(m => m.ID == mq.MessageID);

                            #region 此消息在内存中不存在时，到数据库中取消息
                            if (message == null)
                            {
                                messageString = _httper.Get(string.Format("{0}/api/zcooApi/GetMessageInfoByMessageIDs?messageIDs={1}", apiBaseUrl, mq.MessageID));

                                //把消息，转换为json
                                message = Newtonsoft.Json.JsonConvert.DeserializeObject<IList<Proc_GetMessageInfoByMessageIDs_Result>>(messageString).FirstOrDefault();

                                //如果不为空时，加入到新队列中
                                if (message != null)
                                {
                                    //把刚刚找到的消息，加入消息队列中
                                    messages.Add(message);

                                    if (messages.Count > messageCapacity)
                                    {
                                        messages.RemoveAt(0); //删除第一个
                                    }
                                }
                            }
                            #endregion
                            #endregion

                            #region 发送模板，一直循环
                            while (true)
                            {
                                #region 每次执行前，更新屏幕时间（让机器人有心跳）
                                currentDT = UpdateScreenAndWait(startDT, timesForException, false);
                                #endregion

                                #region 尝试发送模板消息
                                sendWXTemplateMessageResponseModel = SendTemplateMessage(apiBaseUrl, message, mq);
                                #endregion

                                //当返回null，可能是网站访问不了，所以继续循环，直到信息能够发送
                                //微信全局返回码，请见： http://mp.weixin.qq.com/wiki/17/fa4e1434e57290788bde25603fa2fcbd.html
                                if (sendWXTemplateMessageResponseModel == null ||
                                    sendWXTemplateMessageResponseModel.ErrCode == -1 || // 微信服务器系统繁忙。建议重试【见：微信全局返回码中的说明】
                                    sendWXTemplateMessageResponseModel.ErrCode == 42001 || //当AccessToken无效时
                                    sendWXTemplateMessageResponseModel.ErrCode == 40001 //当AccessToken过期时（有可能刚好过期）
                                    )
                                {
                                    continue;
                                }

                                if (sendWXTemplateMessageResponseModel.ErrCode == 0) //正常发送模板消息
                                {
                                    //更新消息为发送成功状态
                                    SetTemplateMessageSuccess(apiBaseUrl, serverID, mq, sendWXTemplateMessageResponseModel);
                                }
                                else if (sendWXTemplateMessageResponseModel.ErrCode == 45009) //调用发送模板消息接口达到上限时
                                {
                                    //设置下次发送时间
                                    templateMessageQueues = SetTemplateMessageQueueNextSendDateTime(templateMessageQueues, mq.PublicAccountID).ToList();
                                }
                                else  //其他情况都视为发送失败
                                {
                                    //标记为删除此循环队列
                                    mq.ShouldDelete = true;

                                    //设置为无效
                                    SetTemplateMessageInvalidPushNo(apiBaseUrl, mq, sendWXTemplateMessageResponseModel);

                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendWXTemplateMessage", (int)SeverityLevelType.NoEffect, sendWXTemplateMessageResponseModel.ErrMsg);
                                }

                                break;
                            }
                            #endregion
                        }
                        #endregion

                        #region 清除消息队列，已经发送成功的
                        if (templateMessageQueues.Count > 0)
                        {
                            Proc_GetRobotServerMessageQueueForWXRobot_Result[] messageQueuesArray = new Proc_GetRobotServerMessageQueueForWXRobot_Result[templateMessageQueues.Count];

                            templateMessageQueues.ToList().CopyTo(messageQueuesArray);

                            foreach (var a in messageQueuesArray)
                            {
                                //只删除那些被标识为“应该删除”的项
                                if (a.ShouldDelete)
                                {
                                    templateMessageQueues.Remove(a);
                                }
                            }
                        }
                        #endregion
                        #endregion
                    }
                    catch (Exception e)
                    {

                        #region 异常时处理
                        int messageFunctionID = 1;

                        if (message != null)
                        {
                            messageFunctionID = message.MessageFunctionID;
                        }

                        LogWriter.WriteLogToDBBySuperAdmin(messageFunctionID, e.StackTrace, (int)SeverityLevelType.VeryUrgent, e.Message);

                        currentDT = UpdateScreenInfo(startDT, false, timesForException);
                        timesForException++;

                        Common.Time.WaitFor.wait(new TimeSpan(0, 0, 0, 0, (int)checkDBIntervalValue));
                        #endregion

                    }

                }
                #endregion

                isSendingTemplateMessage = false;
                isSignalRSendingMessages = false;
            }
            #endregion
        }

        /// <summary>
        /// 后台推送发送短信。
        /// </summary>
        private static void SendShortMessages()
        {
            #region 同步锁，防止多个后台推送并行，保证线程安全
            lock (syncLock)
            {
                #region 总是循环，直接当前消息队列都被清空
                while (true)
                {
                    try
                    {
                        isSendingShortMessage = true;

                        #region 尝试
                        #region 取得新的消息队列
                        var newShortMessageQueues = new List<Proc_GetRobotServerMessageQueueForWXRobot_Result>();
                        newShortMessageQueues = GetMessageQueues(apiBaseUrl, serverID, messageValidPeriodDiff, 0, 5).ToList();

                        #region 有数据时，才执行添加新消息队列
                        if (newShortMessageQueues != null && newShortMessageQueues.Count > 0)
                        {
                            shortMessageQueues.AddRange(newShortMessageQueues);
                        }
                        #endregion
                        #endregion

                        #region 每次执行次，去除重复项
                        shortMessageQueues = shortMessageQueues.DistinctBy(m => m.ID).ToList();
                        #endregion

                        #region 如果内存中没有消息队列,或者没有读取到新的消息队列并且内存中没有可尝试的消息队列，跳出while循环
                        if (shortMessageQueues.Count == 0 || newShortMessageQueues.Count == 0 && (shortMessageQueues.Count > 0 && !HasShouldTryNextSendMessageQueues(shortMessageQueues)))
                        {
                            break;
                        }
                        #endregion

                        #region 按发送时间升级，确保越早的消息，先发送【争取时间】
                        shortMessageQueues = shortMessageQueues.AsQueryable().OrderBy(string.Format("{0},{1}", "MessagePriorityID", "ID")).ToList();
                        #endregion

                        #region 循环消息队列，发送每个条记录
                        foreach (var mq in shortMessageQueues)
                        {
                            currentDT = UpdateScreenAndWait(startDT, timesForException, false);

                            #region 判断当前消息，是否应该尝试的
                            if (!IsShouldTrySendMessageQueue(mq))
                            {
                                continue;
                            }
                            #endregion

                            #region 得到消息内容
                            message = messages.FirstOrDefault(m => m.ID == mq.MessageID);

                            #region 此消息在内存中不存在时，到数据库中取消息
                            if (message == null)
                            {
                                messageString = _httper.Get(string.Format("{0}/api/zcooApi/GetMessageInfoByMessageIDs?messageIDs={1}", apiBaseUrl, mq.MessageID));

                                //把消息，转换为json
                                message = Newtonsoft.Json.JsonConvert.DeserializeObject<IList<Proc_GetMessageInfoByMessageIDs_Result>>(messageString).FirstOrDefault();

                                //如果不为空时，加入到新队列中
                                if (message != null)
                                {
                                    //把刚刚找到的消息，加入消息队列中
                                    messages.Add(message);

                                    if (messages.Count > messageCapacity)
                                    {
                                        messages.RemoveAt(0); //删除第一个
                                    }
                                }
                            }
                            #endregion
                            #endregion

                            while (true)
                            {
                                #region 每次执行前，更新屏幕时间（让机器人有心跳）
                                currentDT = UpdateScreenAndWait(startDT, timesForException, false);
                                #endregion

                                #region 尝试发送短信
                                ShortMessageResponseModel shortMessageResponseModel = new ShortMessageResponseModel() { ReturnCode = -1 };
                                shortMessageResponseModel = SendShortMessage(apiBaseUrl, message, mq);
                                #endregion

                                #region 判断推送号码(Mobile)是否无效
                                var isInvalidPushNo = IsInvalidPushNoForMobile(shortMessageResponseModel);
                                if (isInvalidPushNo)
                                {
                                    var isSuccessfulShortMessage = false; //是否发送成功
                                    var shouldRetryTimes = retryTimes;  //尝试次数
                                    var errorMessage = "sendShortMessageResponseModel返回null";  //错误消息

                                    if (shortMessageResponseModel != null) //不为null
                                    {
                                        errorMessage = ErrorShorMessages(shortMessageResponseModel).ReturnMsg;
                                    }

                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendShortMessage", (int)SeverityLevelType.Urgent, errorMessage);

                                    //尝试多次再设定为无效，防止网络原因，导致一些信息丢失
                                    int currentTryTimes = 0;
                                    #region 循环5次直到成功为此
                                    while (currentTryTimes < shouldRetryTimes)
                                    {
                                        shortMessageResponseModel = SendShortMessage(apiBaseUrl, message, mq);
                                        //记录是否成功
                                        isSuccessfulShortMessage = IsShortMessageSentSuccessfully(shortMessageResponseModel);

                                        //验证是否同样错误
                                        if (!isSuccessfulShortMessage) //不成功，继续尝试
                                        {
                                            currentTryTimes++;
                                        }
                                        else //成功的话，退出此次循环
                                        {
                                            break;
                                        }
                                    }
                                    #endregion

                                    //重置当前重试记录
                                    currentTryTimes = 0;
                                }
                                #endregion

                                #region IP地址限制时
                                if (shortMessageResponseModel != null && shortMessageResponseModel.ReturnCode == 43)
                                {
                                    shortMessageResponseModel = ErrorShorMessages(shortMessageResponseModel);

                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "sendShortMessage", (int)SeverityLevelType.NoEffect, shortMessageResponseModel.ReturnMsg);

                                    //设置下次发送时间
                                    int addMinutes = 5;
                                    shortMessageQueues = SetShortMessageQueueNextSendTime(shortMessageQueues, addMinutes).ToList();

                                    break;
                                }
                                #endregion

                                #region 余额不足
                                else if (shortMessageResponseModel != null && shortMessageResponseModel.ReturnCode == 41)
                                {
                                    shortMessageResponseModel = ErrorShorMessages(shortMessageResponseModel);

                                    LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "sendShortMessage", (int)SeverityLevelType.NoEffect, shortMessageResponseModel.ReturnMsg);

                                    //设置下次发送时间
                                    int addMinutes = 1;
                                    shortMessageQueues = SetShortMessageQueueNextSendTime(shortMessageQueues, addMinutes).ToList();

                                    break;
                                }
                                #endregion

                                #region 正常发送短信消息
                                else if (shortMessageResponseModel != null && shortMessageResponseModel.ReturnCode == 0)
                                {
                                    //设置短信消息为发送成功状态
                                    SetShortMessageSuccess(apiBaseUrl, serverID, mq);

                                    break;
                                }
                                #endregion

                                #region 多次发送短信消息无效时
                                else if (IsInvalidPushNoForMobile(shortMessageResponseModel))
                                {
                                    //标记为删除此循环队列
                                    mq.ShouldDelete = true;

                                    //设置为无效
                                    SetShortMessageInvalidPushNo(apiBaseUrl, mq);

                                    break;
                                }
                                #endregion

                                #region 其他原因失败时。如返回Model为空等情况
                                else
                                {
                                    if (shortMessageResponseModel == null)
                                    {
                                        LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendShortMessage", (int)SeverityLevelType.Normal, "sendShortMessage返回为Null");
                                    }
                                    else
                                    {
                                        LogWriter.WriteLogToDBBySuperAdmin(message.MessageFunctionID, "SendShortMessage", (int)SeverityLevelType.NoEffect, shortMessageResponseModel.ReturnMsg);
                                    }

                                    break;
                                }
                                #endregion
                            }
                        }
                        #endregion

                        #region 清除消息队列，已经发送成功的
                        if (shortMessageQueues.Count > 0)
                        {
                            Proc_GetRobotServerMessageQueueForWXRobot_Result[] messageQueuesArray = new Proc_GetRobotServerMessageQueueForWXRobot_Result[shortMessageQueues.Count];

                            shortMessageQueues.ToList().CopyTo(messageQueuesArray);

                            foreach (var a in messageQueuesArray)
                            {
                                //只删除那些被标识为“应该删除”的项
                                if (a.ShouldDelete)
                                {
                                    shortMessageQueues.Remove(a);
                                }
                            }
                        }
                        #endregion
                        #endregion
                    }
                    catch (Exception e)
                    {

                        #region 异常时处理
                        int messageFunctionID = 1;

                        if (message != null)
                        {
                            messageFunctionID = message.MessageFunctionID;
                        }

                        LogWriter.WriteLogToDBBySuperAdmin(messageFunctionID, e.StackTrace, (int)SeverityLevelType.VeryUrgent, e.Message);

                        currentDT = UpdateScreenInfo(startDT, false, timesForException);
                        timesForException++;

                        Common.Time.WaitFor.wait(new TimeSpan(0, 0, 0, 0, (int)checkDBIntervalValue));
                        #endregion
                    }

                }
                #endregion

                isSendingShortMessage = false;
                isSignalRSendingMessages = false;
            }
            #endregion
        }

        /// <summary>
        /// 内存中消息队列设置的下次发送点已到达
        /// </summary>
        private static void MesssageQuquesNextSentTimeArrived()
        {
            //模板消息内存中有消息，并且有可尝试发送的消息队列
            if (templateMessageQueues.Count > 0 && HasShouldTryNextSendMessageQueues(templateMessageQueues))
            {
                if (!isSendingTemplateMessage)
                {
                    SendTemplateMessages();
                }
            }

            //短信消息内存中有消息，并且有可尝试发送的消息队列
            if (shortMessageQueues.Count > 0 && HasShouldTryNextSendMessageQueues(shortMessageQueues))
            {
                if (!isSendingShortMessage)
                {
                    SendShortMessages();
                }
            }
        }
        #endregion
    }

}
