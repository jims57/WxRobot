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
        /// 判断是否为无效的推送号。
        /// </summary>
        /// <param name="sendWXPreviewMessageResponseModel"></param>
        /// <returns></returns>
        private static bool IsInvalidPushNo(SendWXPreviewMessageResponseModel sendWXPreviewMessageResponseModel)
        {
            var isInvalidPushNo = false;

            if (sendWXPreviewMessageResponseModel == null ||
                    sendWXPreviewMessageResponseModel.Ret == 64501 ||
                    sendWXPreviewMessageResponseModel.Ret == 64502 ||
                    sendWXPreviewMessageResponseModel.Ret == 64503 ||
                    sendWXPreviewMessageResponseModel.Ret == 10703 ||
                    sendWXPreviewMessageResponseModel.Ret == -20000 ||
                    sendWXPreviewMessageResponseModel.Ret == -2 ||
                    sendWXPreviewMessageResponseModel.Ret == -1
                )
            {
                isInvalidPushNo = true;
            }

            return isInvalidPushNo;
        }

        /// <summary>
        /// 判断是否为无效的推送号(模板消息openID)。
        /// </summary>
        /// <param name="sendWXPreviewMessageResponseModel">
        ///40003:invalid openid
        ///40036:invalid template_id size
        ///40037:invalid template_id
        ///47001:data format error
        ///</param>
        /// <returns></returns>
        private static bool IsInvalidPushNoForOpenID(SendWXTemplateMessageResponseModel sendWXTemplateMessageResponseModel)
        {
            var isInvalidPushNo = false;


            if (sendWXPreviewMessageResponseModel == null ||
                sendWXTemplateMessageResponseModel.ErrCode == 40003 ||
                sendWXTemplateMessageResponseModel.ErrCode == 40036 ||
                sendWXTemplateMessageResponseModel.ErrCode == 40037 ||
                sendWXTemplateMessageResponseModel.ErrCode == 47001 ||
                sendWXTemplateMessageResponseModel.ErrCode == -1
                )
            {
                isInvalidPushNo = true;
            }

            return isInvalidPushNo;
        }

        /// <summary>
        /// 判断是否为无效的推送号(短信消息mobile)。
        /// </summary>
        /// <param name="returnCode">
        /// 30：密码错误 
        ///40：账号不存在
        ///41：余额不足
        ///42：帐号过期
        ///43：IP地址限制
        ///50：内容含有敏感词
        ///51：手机号码不正确
        ///</param>
        /// <returns></returns>
        private static bool IsInvalidPushNoForMobile(ShortMessageResponseModel shortMessageResponseModel)
        {
            var isInvalidPushNo = false;

            if (shortMessageResponseModel == null ||
                shortMessageResponseModel.ReturnCode == -1 ||
                shortMessageResponseModel.ReturnCode == 30 ||
                shortMessageResponseModel.ReturnCode == 40 ||
                shortMessageResponseModel.ReturnCode == 42 ||
                shortMessageResponseModel.ReturnCode == 50 ||
                shortMessageResponseModel.ReturnCode == 51
                )
            {
                isInvalidPushNo = true;
            }

            return isInvalidPushNo;
        }

        /// <summary>
        /// 定制短信错误消息
        /// </summary>
        /// <param name="shortMessageResponseModel"></param>
        /// <returns></returns>
        private static ShortMessageResponseModel ErrorShorMessages(ShortMessageResponseModel shortMessageResponseModel)
        {
            ShortMessageResponseModel shorMessageModel = new ShortMessageResponseModel();
            shorMessageModel.ReturnCode = shortMessageResponseModel.ReturnCode;

            switch (shorMessageModel.ReturnCode)
            {
                case 30: shorMessageModel.ReturnMsg = "密码错误";
                    break;
                case 40: shorMessageModel.ReturnMsg = "账号不存在";
                    break;
                case 41: shorMessageModel.ReturnMsg = "余额不足";
                    break;
                case 42: shorMessageModel.ReturnMsg = "帐号过期";
                    break;
                case 43: shorMessageModel.ReturnMsg = "IP地址限制";
                    break;
                case 50: shorMessageModel.ReturnMsg = "内容含有敏感词";
                    break;
                case 51:
                case -1: shorMessageModel.ReturnMsg = "手机号码不正确";
                    break;
                default:
                    shorMessageModel.ReturnMsg = "其他情况错误";
                    break;
            }

            return shorMessageModel;

        }
    }
}
