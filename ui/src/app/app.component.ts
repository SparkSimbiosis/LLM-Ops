import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChatComponent } from './chat.component';
import { ConsoleComponent } from './console.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, ChatComponent, ConsoleComponent],
  template: `
    <div class="wrap">
      <header class="top">
        <div class="brand">
          <span class="logo">◐</span>
          <span class="name">SupportGW</span>
          <span class="tag">LLMOps capstone</span>
        </div>
        <div class="health" [class.ok]="healthy === true" [class.bad]="healthy === false">
          <span class="dot"></span>
          {{ healthy === null ? 'перевірка…' : healthy ? 'сервіс онлайн' : 'сервіс недоступний' }}
        </div>
      </header>
      <div class="cols">
        <section><app-chat></app-chat></section>
        <section><app-console></app-console></section>
      </div>
    </div>
  `,
  styles: [
    `
      .wrap { max-width: 1200px; margin: 18px auto; padding: 0 16px; }
      .top { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
      .brand { display: flex; align-items: center; gap: 10px; }
      .logo { width: 34px; height: 34px; border-radius: 10px; background: #4038c4; color: #fff; display: grid; place-items: center; font-size: 18px; }
      .name { font-weight: 800; font-size: 19px; letter-spacing: -0.01em; }
      .tag { font-size: 12px; color: #8a90a0; background: #fff; border: 1px solid #e4e6ec; padding: 3px 9px; border-radius: 20px; }
      .health { display: flex; align-items: center; gap: 7px; font-size: 13px; color: #8a90a0; }
      .health .dot { width: 9px; height: 9px; border-radius: 50%; background: #c2c6d0; }
      .health.ok { color: #177245; }
      .health.ok .dot { background: #177245; box-shadow: 0 0 0 3px rgba(23, 114, 69, 0.15); }
      .health.bad { color: #b3261e; }
      .health.bad .dot { background: #b3261e; }
      .cols { display: grid; grid-template-columns: minmax(0, 0.85fr) minmax(0, 1.15fr); gap: 20px; align-items: start; }
      @media (max-width: 900px) { .cols { grid-template-columns: 1fr; } }
    `,
  ],
})
export class AppComponent implements OnInit, OnDestroy {
  healthy: boolean | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    const check = () =>
      this.http.get('/api/health').subscribe({
        next: () => (this.healthy = true),
        error: () => (this.healthy = false),
      });
    check();
    this.timer = setInterval(check, 5000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }
}
