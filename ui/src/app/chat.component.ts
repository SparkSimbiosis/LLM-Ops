import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

interface Msg {
  role: 'user' | 'bot';
  text: string;
  latency?: number;
  tool?: string | null;
}

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="chat">
      <div class="head">
        <span class="ava">◐</span>
        <div>
          <div class="ttl">Підтримка</div>
          <div class="sub">відповідає за кілька секунд</div>
        </div>
      </div>
      <div class="body">
        <ng-container *ngFor="let m of messages">
          <div class="row" [class.user]="m.role === 'user'">
            <div class="msg" [class.user]="m.role === 'user'">{{ m.text }}</div>
          </div>
          <div class="meta" *ngIf="m.role === 'bot' && (m.latency !== undefined || m.tool)">
            <span *ngIf="m.latency !== undefined">{{ m.latency }}ms</span>
            <span class="chip" *ngIf="m.tool">🔧 {{ m.tool }}</span>
          </div>
        </ng-container>
        <div class="row" *ngIf="busy">
          <div class="msg typing"><span></span><span></span><span></span></div>
        </div>
      </div>
      <form class="input" (submit)="send($event)">
        <input [(ngModel)]="draft" name="draft" placeholder="Напишіть повідомлення…" autocomplete="off" [disabled]="busy" />
        <button type="submit" [disabled]="busy">➤</button>
      </form>
    </div>
  `,
  styles: [
    `
      .chat { border: 1px solid #e4e6ec; border-radius: 14px; overflow: hidden; background: #fff; box-shadow: 0 1px 2px rgba(20,20,30,.04), 0 8px 24px rgba(20,20,30,.05); }
      .head { display: flex; align-items: center; gap: 10px; padding: 12px 16px; background: #f6f7f9; border-bottom: 1px solid #eceef2; }
      .ava { width: 34px; height: 34px; border-radius: 50%; background: #4038c4; color: #fff; display: grid; place-items: center; font-size: 16px; }
      .ttl { font-weight: 650; font-size: 14px; }
      .sub { font-size: 11.5px; color: #8a90a0; }
      .body { padding: 16px; display: flex; flex-direction: column; gap: 4px; min-height: 300px; max-height: 480px; overflow-y: auto; background: #fbfbfd; }
      .row { display: flex; margin-top: 6px; }
      .row.user { justify-content: flex-end; }
      .msg { max-width: 82%; padding: 9px 13px; border-radius: 14px; border-bottom-left-radius: 5px; background: #fff; border: 1px solid #e9eaef; font-size: 14px; line-height: 1.45; }
      .msg.user { background: #4038c4; color: #fff; border: none; border-radius: 14px; border-bottom-right-radius: 5px; }
      .meta { display: flex; gap: 8px; padding: 3px 4px 0; font-size: 11px; color: #a0a4b0; font-family: ui-monospace, monospace; }
      .chip { color: #4038c4; background: #ecebfb; padding: 1px 7px; border-radius: 12px; }
      .typing { display: flex; gap: 4px; align-items: center; padding: 12px 14px; }
      .typing span { width: 6px; height: 6px; border-radius: 50%; background: #b9bcc7; animation: blink 1.2s infinite; }
      .typing span:nth-child(2) { animation-delay: 0.2s; }
      .typing span:nth-child(3) { animation-delay: 0.4s; }
      @keyframes blink { 0%, 80%, 100% { opacity: 0.3; } 40% { opacity: 1; } }
      .input { display: flex; gap: 8px; padding: 12px; border-top: 1px solid #eceef2; background: #fff; }
      .input input { flex: 1; padding: 9px 14px; border: 1px solid #dfe1e8; border-radius: 22px; font: inherit; outline: none; }
      .input input:focus { border-color: #4038c4; }
      .input button { border: none; background: #4038c4; color: #fff; border-radius: 50%; width: 38px; height: 38px; cursor: pointer; font-size: 14px; }
      .input button:disabled { opacity: 0.5; cursor: default; }
    `,
  ],
})
export class ChatComponent {
  messages: Msg[] = [{ role: 'bot', text: 'Вітаю! Чим можу допомогти?' }];
  draft = '';
  busy = false;

  constructor(private http: HttpClient) {}

  send(e: Event): void {
    e.preventDefault();
    const text = this.draft.trim();
    if (!text || this.busy) return;
    this.messages.push({ role: 'user', text });
    this.draft = '';
    this.busy = true;
    this.http.post<{ content: string; latency_ms: number; tool: string | null }>('/api/chat', { message: text }).subscribe({
      next: (r) => {
        this.messages.push({ role: 'bot', text: r.content, latency: r.latency_ms, tool: r.tool });
        this.busy = false;
      },
      error: () => {
        this.messages.push({ role: 'bot', text: 'Помилка зʼєднання.' });
        this.busy = false;
      },
    });
  }
}
