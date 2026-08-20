using System.Text.Json;

namespace LavaTranslator.Services;

/// <summary>
/// 为内嵌网页翻译站生成“自动填充原文输入框”的 JavaScript。
/// 每个站点优先使用专用选择器，找不到时退回通用启发式匹配，
/// 同时尽可能触发站点框架能识别的事件（含翻译按钮点击）。
/// </summary>
public static class WebFillAutomation
{
    public static string BuildFillScript(string siteId, string text)
    {
        return Template
            .Replace("__SITE__", siteId)
            .Replace("__TEXT__", JsonSerializer.Serialize(text));
    }

    private const string Template = """
        (function () {
          "use strict";
          var TEXT = __TEXT__;
          var SITE = "__SITE__";

          var CONFIGS = {
            sogou:   { inputs: ["#trans-input"], button: "" },
            youdao:  { inputs: ["#js_trans_input", "textarea[placeholder*='翻译']", "textarea[aria-label*='翻译']"], button: "#js_fanyi" },
            baidu:   { inputs: ["#baidu_translate_input", "div[contenteditable='true'][role='textbox']", "[contenteditable='true']"], button: ".Dmn89frm" },
            bing:    { inputs: ["#tta_input_ta", "textarea#t_src", "#tta_input [contenteditable='true']"], button: "" },
            google:  { inputs: [".er8xn", "textarea[aria-label*='Source text' i]", "textarea#source"], button: "" },
            deepl:   { inputs: ["d-textarea[name='source'] [contenteditable='true']", "d-textarea[name='source']", "textarea[aria-label*='Source text' i]"], button: "" },
            tencent: { inputs: ["#js_originaltext", "textarea[placeholder*='原文' i]", "textarea[aria-label*='原文' i]"], button: "#js_translate_btn" }
          };

          function isVisible(el) {
            if (!el) return false;
            var st = window.getComputedStyle(el);
            if (st.display === "none" || st.visibility === "hidden") return false;
            var r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0;
          }

          function findInput(selectors) {
            for (var i = 0; i < selectors.length; i++) {
              var list;
              try { list = document.querySelectorAll(selectors[i]); } catch (e) { continue; }
              for (var j = 0; j < list.length; j++) {
                var el = list[j];
                if (isVisible(el) && !el.readOnly && !el.disabled) return el;
              }
            }
            return null;
          }

          function findGeneric() {
            var best = null;
            var bestScore = -1000;
            function consider(el, score) {
              if (!isVisible(el)) return;
              if (score > bestScore) { bestScore = score; best = el; }
            }
            var textareas = document.querySelectorAll("textarea");
            for (var i = 0; i < textareas.length; i++) {
              var t = textareas[i];
              if (t.readOnly || t.disabled) continue;
              var score = 10;
              var ph = ((t.getAttribute("placeholder") || "") + " " + (t.getAttribute("aria-label") || "")).toLowerCase();
              var idCls = ((t.id || "") + " " + (t.className || "")).toLowerCase();
              if (/(source|translate|trans|enter text|原文|翻译|请输入文本)/.test(ph)) score += 100;
              if (/(input|source|trans|原文|翻译)/.test(idCls)) score += 40;
              if (/(output|result|译文)/.test(idCls)) score -= 200;
              consider(t, score);
            }
            var editables = document.querySelectorAll('[contenteditable="true"], [contenteditable="plaintext-only"]');
            for (var k = 0; k < editables.length; k++) {
              var c = editables[k];
              var score2 = 0;
              if ((c.getAttribute("role") || "").toLowerCase() === "textbox") score2 += 60;
              var ph2 = ((c.getAttribute("data-placeholder") || "") + " " + (c.getAttribute("aria-label") || "")).toLowerCase();
              if (/(source|translate|enter text|原文|翻译|请输入文本)/.test(ph2)) score2 += 100;
              var idCls2 = ((c.id || "") + " " + (c.className || "")).toLowerCase();
              if (/(output|result|译文)/.test(idCls2)) score2 -= 200;
              try {
                if (c.closest('[id*="output" i], [class*="output" i], [id*="result" i], [class*="result" i]')) score2 -= 100;
              } catch (e) {}
              consider(c, score2);
            }
            return best;
          }

          function setNativeValue(el) {
            var proto = el.tagName.toLowerCase() === "textarea" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            var desc = Object.getOwnPropertyDescriptor(proto, "value");
            if (desc && desc.set) {
              desc.set.call(el, TEXT);
            } else {
              el.value = TEXT;
            }
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
          }

          function setEditableValue(el) {
            el.focus();
            try {
              var range = document.createRange();
              range.selectNodeContents(el);
              var sel = window.getSelection();
              sel.removeAllRanges();
              sel.addRange(range);
            } catch (e) {}
            if (document.execCommand) {
              document.execCommand("insertText", false, TEXT);
              return;
            }
            el.textContent = TEXT;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
          }

          function clickButton(selector) {
            if (!selector) return;
            setTimeout(function () {
              var btn;
              try { btn = document.querySelector(selector); } catch (e) { return; }
              if (btn && isVisible(btn) && !btn.disabled) btn.click();
            }, 150);
          }

          var cfg = CONFIGS[SITE] || { inputs: [], button: "" };
          var el = findInput(cfg.inputs || []);
          var method = "site";
          if (!el) {
            el = findGeneric();
            method = "generic";
          }
          if (!el) return { ok: false, detail: "not-found" };
          try {
            var tag = el.tagName.toLowerCase();
            if (tag === "textarea" || tag === "input") {
              setNativeValue(el);
            } else if (el.getAttribute("contenteditable") !== null || el.isContentEditable) {
              setEditableValue(el);
            } else {
              return { ok: false, detail: "unsupported-element" };
            }
            try { el.scrollIntoView({ block: "center" }); } catch (e) {}
            clickButton(cfg.button);
            return { ok: true, method: method, tag: tag };
          } catch (e) {
            return { ok: false, detail: String(e && e.message || e) };
          }
        })();
        """;
}
