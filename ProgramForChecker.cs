using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Models.WX.MessageModel.PreviewMessage;
using Models.WX.MessageModel.TemplateMessage;
using Models.ShortMessage;

namespace WXRobot
{
    public partial class Program
    {
        /// <summary>
        /// 判断预览消息发送是否成功。
        /// </summary>
        /// <param name="sendWXPreviewMessageResponseModel"></param>
        /// <returns></returns>
        public static bool IsPreviewMessageSentSuccessfully(SendWXPreviewMessageResponseModel sendWXPreviewMessageResponseModel)
        {
            bool isSuccess = false;

            //判断是否成功
            if (sendWXPreviewMessageResponseModel != null && sendWXPreviewMessageResponseModel.Ret == 0) //不为空，并且错误码为 0时
            {
                isSuccess = true;
            }

            return isSuccess;
        }

        /// <summary>
        /// 判断模板消息发送是否成功。
        /// </summary>
        /// <param name="sendWXTemplateMessageResponseModel"></param>
        /// <returns></returns>
        public static bool IsTemplateMessageSentSuccessfully(SendWXTemplateMessageResponseModel sendWXTemplateMessageResponseModel)
        {
            bool isSuccess = false;

            //判断是否成功
            if (sendWXTemplateMessageResponseModel != null && sendWXTemplateMessageResponseModel.ErrCode == 0) //不为空，并且错误码为 0时
            {
                isSuccess = true;
            }

            return isSuccess;
        }

        /// <summary>
        /// 判断短信消息发送是否成功。
        /// </summary>
        /// <param name="shortMessageResponseModel"></param>
        /// <returns></returns>
        public static bool IsShortMessageSentSuccessfully(ShortMessageResponseModel shortMessageResponseModel)
        {
            bool isSuccess = false;

            //判断是否成功
            if (shortMessageResponseModel != null && shortMessageResponseModel.ReturnCode == 0) //不为空，并且错误码为 0时
            {
                isSuccess = true;
            }

            return isSuccess;
        }
    }
}
