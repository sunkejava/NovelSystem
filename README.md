# NovelSystem

基于 .NET 10 + Vue 3 + SQLite + llama.cpp + Qwen3-TTS 的本地智能小说系统。

## 核心能力

- 上传 TXT 小说并持久化保存全文
- 配置本地 AI 地址，默认兼容 llama.cpp 的 OpenAI Chat Completions API
- 分块解析长篇小说，自动提取人物、人物属性、对白/旁白脚本、情绪信息
- 不同人物绑定不同 WAV 参考音色
- 音色目录可配置，系统自动扫描目录下所有 .wav 文件
- 调用本地 Qwen3-TTS 逐句生成音频
- 后台任务中心保存小说解析、音频生成任务及进度/错误信息
- FFmpeg 按脚本顺序合并完整 MP3
- 学习已有小说的写作方法、叙事风格、节奏和人物塑造方法
- 基于学习到的写作方法续写或生成全新小说
- AI 生成小说入库、TXT 下载，并可重新加入小说库继续解析和生成有声书
- 所有核心业务数据使用 SQLite 入库，便于后续继续操作与扩展

## 技术栈

后端：

- .NET 10
- ASP.NET Core Minimal API
- EF Core 10
- SQLite
- BackgroundService + 内存任务队列
- HttpClient
- FFmpeg

前端：

- Vue 3
- TypeScript
- Vite
- Element Plus
- Axios

## 目录

    src/backend/NovelSystem.Api
    src/frontend
    data
    storage/audio
    storage/output
    voices

其中 data、storage、voices 为运行时目录，不提交到 Git。

## 1. 启动 llama.cpp

示例：

    llama-server.exe ^
      -m "D:\Models\model.gguf" ^
      --host 127.0.0.1 ^
      --port 8080 ^
      -c 32768 ^
      -ngl 999

NovelSystem 默认配置：

    http://127.0.0.1:8080/v1

默认模型名：

    local-model

如果 llama.cpp 启动时配置了 --alias，请在系统设置中把 AiModel 改成对应 alias。

## 2. Qwen3-TTS

默认 TTS 服务地址：

    http://127.0.0.1:7860

当前适配器默认调用：

    POST /api/tts

请求体：

    {
      "text": "需要合成的文本",
      "ref_audio": "voices/人物音色.wav",
      "language": "zh"
    }

并期望接口直接返回 WAV 二进制。

如果你当前运行的 Qwen3-TTS WebUI/API 字段或 URL 不同，只需要修改后端 Program.cs 中 TtsClient，不需要调整小说解析、人物、任务或前端业务。

## 3. 音色目录

默认目录：

    voices

可在前端“系统设置 -> 音色 WAV 目录”修改为绝对路径，例如：

    D:\AIFiles\qwen3-tts\voices

系统会读取该目录第一层所有 .wav 文件，并在人物配置中提供选择。

## 4. FFmpeg

需要安装 FFmpeg，并保证 ffmpeg 在 PATH 中。

也可以在系统设置中填写：

    D:\Tools\ffmpeg\bin\ffmpeg.exe

## 5. 启动后端

    cd src/backend/NovelSystem.Api
    dotnet restore
    dotnet run --urls http://0.0.0.0:5080

API 地址：

    http://localhost:5080

## 6. 启动前端

    cd src/frontend
    npm install
    npm run dev

访问：

    http://localhost:5173

如后端地址不是 5080，可设置：

    VITE_API_URL=http://你的服务器:端口/api

## 使用流程

### 小说转有声书

1. 上传 TXT 小说
2. 进入小说详情
3. 点击“AI解析人物/脚本”
4. 在任务中心等待解析结束
5. 返回小说详情
6. 为人物分别选择 WAV 音色
7. 点击“生成整书音频”
8. 系统逐句调用 Qwen3-TTS
9. 音频写入 storage/audio/{novelId}
10. FFmpeg 合并输出 storage/output/novel-{novelId}.mp3

### 学习写作方法

1. 在小说库点击“学习写法”
2. llama.cpp 分析小说的：
   - 叙事视角
   - 语言风格
   - 章节结构
   - 情节节奏
   - 人物塑造
   - 对白特征
   - 悬念设置
   - 情绪推进
3. 写作风格结果保存到数据库

### AI 生成小说

1. 进入 AI 创作
2. 选择已学习写作风格
3. 输入新小说要求
4. 调用本地 AI 生成新内容
5. 结果保存到 GeneratedNovel
6. 可下载 TXT
7. 可调用 publish API 加入小说库
8. 加入后继续执行人物解析和有声书生成

## 数据模型

当前核心表包括：

- Novel
- Character
- ScriptLine
- JobRecord
- Setting
- WritingStyle
- GeneratedNovel

## API 摘要

    GET    /api/novels
    POST   /api/novels/upload
    GET    /api/novels/{id}
    POST   /api/novels/{id}/analyze
    POST   /api/novels/{id}/audio

    PUT    /api/characters/{id}

    GET    /api/jobs
    GET    /api/jobs/{id}/download

    GET    /api/settings
    PUT    /api/settings
    GET    /api/settings/voices

    POST   /api/writing/learn/{id}
    GET    /api/writing/styles
    POST   /api/writing/generate
    GET    /api/writing/generated
    GET    /api/writing/generated/{id}/download
    POST   /api/writing/generated/{id}/publish

## 当前设计说明

该版本优先完成完整业务闭环，并保持 AI/TTS 可替换：

    小说业务
        |
        +-- AiClient -> llama.cpp / OpenAI-Compatible
        |
        +-- TtsClient -> Qwen3-TTS
        |
        +-- JobWorker -> 长任务执行
        |
        +-- EF Core -> SQLite
        |
        +-- FFmpeg -> MP3 合并

后续可以继续拆成 DDD/Clean Architecture 项目，并增加：

- 用户注册/登录/JWT
- RBAC
- 多用户数据隔离
- 章节表和卷表
- 任务暂停/取消/重试
- 服务重启后任务恢复
- SignalR 实时进度
- TTS GPU 并发队列
- 旁白默认音色
- 单句试听和重新生成
- 音量归一化
- 停顿参数
- BGM
- SRT/ASS 字幕
- 长小说断点续跑
- PostgreSQL/MySQL
- Docker Compose
- Windows/Linux 单文件发布
