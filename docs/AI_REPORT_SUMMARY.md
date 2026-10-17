# Turn your saved activity into a summary

Choose a period and the text you want to use. WorkTrail can ask your AI provider to turn it into a summary that you can read, edit and include in your export.

**Only text is sent. Your screenshot images and activity database stay on your computer.** There is a size limit, so choosing a long period will not upload gigabytes of saved screenshots.

## You choose when to send it

Nothing is sent for this summary until you choose **Generate summary**. You can change dates, browse the preview and choose your options first. Opening this guide opens GitHub in your browser without sending your activity to it.

The summary uses the AI provider and model you configured in settings. Your provider may charge for it, including an unsuccessful attempt. Choosing Generate again sends a new request. WorkTrail checks its daily spending limit, but does not show a price estimate for this summary before sending it.

## Choose which text to include

The dates and device you select determine which saved captures are used. Both the start and end dates are included, in the selected time zone. The checkboxes let you choose:

- **Saved AI descriptions:** descriptions already created for your screenshots.
- **Window titles:** the titles of the windows you were using.

Each included entry also carries its date and application name. Entries without any of your chosen text are skipped. Exact repeats with the same date, application and selected text are included only once. If there is no matching text, WorkTrail tells you instead of sending a request.

**Check what your saved text may contain.** Descriptions can include private messages, names, passwords or file locations. WorkTrail asks the AI not to repeat sensitive details, but it does not remove them from the text before sending it. Turning off Window titles does not remove titles already mentioned inside a description.

These choices belong to the AI summary. Hiding the Captures table from your exported file does not exclude those captures from the summary. Likewise, seeing only five rows in the file preview does not mean only five entries will be sent.

## What happens if you select too much

WorkTrail checks the selected text before sending it. The limit is **160,000 characters**, counting the text together with the labels and formatting used to prepare it for the AI. That means it is not an allowance of exactly 160,000 typed letters. The instructions for the AI add some extra text, and the app does not show the exact upload size.

If your selection is too large, WorkTrail stops before sending it. Choose fewer days, one device or fewer text sources, then try again. It does not quietly cut your text or split it into several paid requests. Your chosen AI model may have a smaller limit of its own.

There is also a separate limit when reading saved activity on your computer: 50,000 captures and 32 million characters across saved descriptions. This check includes saved text even when its summary checkbox is off. These larger limits protect the local read; they do not increase how much can be sent to the AI.

## Make the summary useful to you

Choose **Day** for a day-by-day account, **Application** to organize it by app, or **Whole period** for an overall summary. This changes how the AI is asked to write the result, not how much source text is sent.

**Detailed summary** asks for up to 1,800 words. Turning it off asks for up to 600. Both use the same selected text, so choosing a shorter answer does not reduce the text you share. These lengths are requests to the AI, not guaranteed word counts; your AI settings also limit how long its answer can be.

WorkTrail asks the AI to use your selected language and describe what was observed. It should not invent time spent, completed tasks or conclusions about your productivity. Still, it can make mistakes: **read and edit the draft before including it in your export**.

If the provider returns an empty answer, marks it as unfinished or returns more than 60,000 characters, WorkTrail shows an error instead of accepting the draft.

## Code references

This guide describes the current implementation. Follow these references when checking a different revision:

- [ReportSummaryService](../WorkTrail.Core/Infrastructure/Services/ReportSummaryService.cs): selected fields, deduplication, 160,000-character limit, prompt, image-free call, output validation and usage accounting.
- [ReportExportService](../WorkTrail.Core/Infrastructure/Services/ReportExportService.cs): date/device filters, capture and local text limits, five-row preview.
- [Application export operations](../WorkTrail.Core/Application/WorkTrailApplication.Export.cs): AI configuration, daily cost gate and serialized usage persistence.
- [Export window](../WorkTrail/ReportExportWindow.xaml.cs): explicit generation, separate summary selections and editable output.
- [OpenAI adapter](../WorkTrail.Core/Infrastructure/Services/OpenAiDecoder.cs), [OpenRouter adapter](../WorkTrail.Core/Infrastructure/Services/OpenRouterDecoder.cs), [Anthropic adapter](../WorkTrail.Core/Infrastructure/Services/AnthropicDecoder.cs): provider request serialization and output-token settings.
