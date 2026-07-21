# 钉钉AI表格写入：Python 接口契约（供 C# 客户端对接）

Base URL：由 C# 端配置（跟其它接口一样走设置界面/PersistentParams，不要写死）。
所有接口请求头都要带鉴权：

```
X-Api-Key: <约定好的 Key>
```

请求体、响应体统一 `application/json`。

底层钉钉的鉴权（accessToken 获取/缓存）、账号转 unionId、字段格式全部由 Python 侧封装，
C# 端不需要直接对接钉钉开放平台，只调这 3 个接口即可。

---

## 一、账号转 unionId（可选，一般不用单独调）

```
GET {baseUrl}/dingtalk/user/unionid?account={账号}
```

传手机号或钉钉 userid，返回对应的 unionId。**大多数场景不需要单独调这个接口**——
下面的"写入记录"和"获取附件上传信息"接口内部会自动完成账号转 unionId，
这个接口主要用于调试/自查。

### 响应体

```json
{
  "account": "17702713290",
  "unionId": "cM8iPMIfj84PBtiidmoqK9SQiEiE"
}
```

---

## 二、写入AI表格记录

```
POST {baseUrl}/dingtalk/records
```

### 请求体

```json
{
  "account": "17702713290",
  "records": [
    {
      "电话/ID/订单号": "13900001234",
      "问题描述": "订单一直没发货"
    }
  ]
}
```

字段说明：

| 字段 | 必填 | 说明 |
|---|---|---|
| `account` | 是 | 提交人账号，手机号或钉钉 userid，Python 侧会自动查出对应的 unionId 作为操作人 |
| `records` | 是 | 数组，可以一次提交多行。每个元素是一行记录的字段字典，**key 必须是钉钉表格里实际的列名**（比如"电话/ID/订单号"），value 是这一列要写的值。写哪些列、列名叫什么，由 C# 端自己按实际表格结构组装，Python 侧不做字段映射 |

写附件字段时，value 的格式是一个数组（见下面"附件上传"部分第 3 步）。

### 响应体

```json
{
  "data": ["KPdJ7xqWXj"],
  "success": true,
  "error": ""
}
```

`data` 是写入成功的记录 ID 数组，跟 `records` 数组一一对应。

### 失败响应

HTTP 状态码非 200，或 500 报错时会带上钉钉原始错误信息，比如权限不足、字段名不存在、表不存在等，
C# 端按 HTTP 状态码判断失败即可，报错信息直接透传给用户/日志。

---

## 三、写附件（三步）

### 第 1 步：获取上传信息

```
POST {baseUrl}/dingtalk/upload-info
```

请求体：

```json
{
  "account": "17702713290",
  "size": 20480,
  "mediaType": "image/jpeg",
  "resourceName": "screenshot.jpg"
}
```

| 字段 | 说明 |
|---|---|
| `account` | 同上，提交人账号 |
| `size` | 文件大小，单位字节 |
| `mediaType` | 文件的 MIME 类型，比如 `image/jpeg`、`application/pdf` |
| `resourceName` | 文件名 |

响应体：

```json
{
  "data": {
    "uploadUrl": "https://xxx.dingtalk.com/...",
    "resourceUrl": "https://xxx.dingtalk.com/...",
    "resourceId": "9ee6c515-4ebd-47a0-b8c3-5383c4241"
  },
  "success": true,
  "error": ""
}
```

### 第 2 步：C# 端直接把文件本体 PUT 到 `uploadUrl`

**不经过 Python 服务器中转**，C# 直接用 `HttpClient` 把本地文件用 PUT 请求传到 `uploadUrl`，
`Content-Type` 传实际的文件 MIME 类型。示例（等价于 cURL）：

```
PUT {uploadUrl}
Content-Type: image/jpeg
Body: <文件二进制内容>
```

### 第 3 步：调"写入AI表格记录"接口，把附件写进对应列

附件列的值格式是一个数组（哪怕只有一个文件也要包成数组）：

```json
{
  "account": "17702713290",
  "records": [
    {
      "相关材料": [
        {
          "filename": "screenshot.jpg",
          "size": 20480,
          "type": "image/jpeg",
          "url": "第1步返回的 resourceUrl（注意不是 uploadUrl）",
          "resourceId": "第1步返回的 resourceId"
        }
      ]
    }
  ]
}
```

多个附件就在数组里加多个元素，每个元素都要走一遍第 1、2 步拿到自己的 `resourceUrl`/`resourceId`。

---

## 边界情况

- `account` 查不到对应的钉钉用户、或应用权限不足：接口返回非 200，错误信息里会带钉钉原始报错（比如"未找到该用户"、"应用尚未开通所需的权限"），C# 端捕获异常按失败处理，不要重试（权限类错误重试也没用，需要人工去钉钉后台处理）。
- `records` 里的列名/表结构如果跟钉钉表格实际结构不一致，会报 404 或参数错误，C# 端提交前建议跟实际表格核对一遍列名。
