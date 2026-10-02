import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { catchError } from 'rxjs/operators';

@Component({
  selector: 'app-console',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="panel-h">LLMOps-консоль <span class="sub">оновлюється кожні {{ intervalSec }}с</span></div>

    <div class="tiles">
      <div class="tile">
        <div class="k">Вартість сьогодні</div>
        <div class="v">{{ money(cost?.today_usd) }} <span class="u">/ {{ money(cost?.budget_usd) }}</span></div>
        <div class="bar" *ngIf="budgetPct() !== null"><i [style.width.%]="budgetPct()"></i></div>
      </div>
      <div class="tile">
        <div class="k">p95 latency</div>
        <div class="v" [class.warn]="isNum(obs?.p95_ms) && obs.p95_ms > 1500">{{ num(obs?.p95_ms) }}<span class="u">ms</span></div>
      </div>
      <div class="tile"><div class="k">Запити</div><div class="v">{{ num(obs?.requests) }}</div></div>
      <div class="tile">
        <div class="k">Cache-hit</div>
        <div class="v" [class.good]="isNum(obs?.cache_hit_pct) && obs.cache_hit_pct > 0">{{ num(obs?.cache_hit_pct) }}<span class="u">%</span></div>
      </div>
      <div class="tile">
        <div class="k">Error rate</div>
        <div class="v" [class.crit]="isNum(obs?.error_rate_pct) && obs.error_rate_pct > 5">{{ num(obs?.error_rate_pct) }}<span class="u">%</span></div>
      </div>
      <div class="tile">
        <div class="k">Fallback</div>
        <div class="v" [class.warn]="isNum(obs?.fallback_events) && obs.fallback_events > 0">{{ num(obs?.fallback_events) }}</div>
      </div>
    </div>

    <div class="card">
      <div class="ch">Prompt registry <span class="lens">L02</span></div>
      <ng-container *ngIf="prompts?.length; else emptyP">
        <div class="row" *ngFor="let p of prompts">
          <span class="mono">{{ p.name }}</span><span class="mono">{{ p.version }}</span>
          <span class="badge" [class.on]="p.active">{{ p.active ? 'active' : 'archived' }}</span>
          <button class="act" *ngIf="!p.active" (click)="activate(p.version)">зробити активною</button>
        </div>
      </ng-container>
      <ng-template #emptyP><div class="empty">— очікує GET /prompts (HW1)</div></ng-template>
    </div>

    <div class="card">
      <div class="ch">Надійність провайдерів <span class="lens">L07</span></div>
      <ng-container *ngIf="providers?.length; else emptyH">
        <div class="prov" *ngFor="let p of providers"><span class="dot" [class.down]="p.status !== 'ok'"></span>{{ p.name }} · {{ p.status }}</div>
      </ng-container>
      <ng-template #emptyH><div class="empty">— очікує GET /providers (HW5)</div></ng-template>
    </div>

    <div class="card">
      <div class="ch">Approvals · HITL <span class="lens">L08</span></div>
      <ng-container *ngIf="approvals?.length; else emptyA">
        <div class="row" *ngFor="let a of approvals">
          <span class="mono">{{ a.id }}</span> {{ a.action }}
          <button class="act ok" (click)="approve(a.id)">approve</button>
        </div>
      </ng-container>
      <ng-template #emptyA><div class="empty">— черга порожня</div></ng-template>
    </div>

    <p class="foot">Read-only вітрина + дії approve/rollback. Дані з API сервісу; «—» означає, що ендпоінт ще заглушка — реалізуй його, і плитка оживе.</p>
  `,
  styles: [
    `
      :host { display: block; }
      .panel-h { font-family: ui-monospace, monospace; font-size: 12px; text-transform: uppercase; letter-spacing: 0.1em; color: #8a90a0; margin-bottom: 12px; }
      .panel-h .sub { text-transform: none; letter-spacing: 0; }
      .tiles { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-bottom: 12px; }
      @media (max-width: 560px) { .tiles { grid-template-columns: repeat(2, 1fr); } }
      .tile { background: #fff; border: 1px solid #e4e6ec; border-radius: 12px; padding: 12px 13px; box-shadow: 0 1px 2px rgba(20,20,30,.03); }
      .tile .k { font-size: 11px; color: #8a90a0; text-transform: uppercase; letter-spacing: 0.04em; }
      .tile .v { font-family: ui-monospace, monospace; font-size: 20px; font-weight: 700; margin-top: 5px; }
      .tile .v.good { color: #177245; }
      .tile .v.warn { color: #9a5b12; }
      .tile .v.crit { color: #b3261e; }
      .tile .u { font-size: 12px; color: #8a90a0; font-weight: 600; }
      .bar { height: 4px; background: #eef0f4; border-radius: 4px; margin-top: 8px; overflow: hidden; }
      .bar i { display: block; height: 100%; background: #4038c4; border-radius: 4px; transition: width 0.4s; }
      .card { background: #fff; border: 1px solid #e4e6ec; border-radius: 12px; padding: 14px; margin-bottom: 12px; box-shadow: 0 1px 2px rgba(20,20,30,.03); }
      .ch { font-weight: 650; font-size: 14px; margin-bottom: 10px; display: flex; justify-content: space-between; align-items: center; }
      .lens { font-family: ui-monospace, monospace; font-size: 10px; color: #4038c4; background: #ecebfb; padding: 2px 7px; border-radius: 20px; }
      .row { display: flex; gap: 10px; align-items: center; font-size: 13px; padding: 4px 0; }
      .mono { font-family: ui-monospace, monospace; }
      .badge { font-family: ui-monospace, monospace; font-size: 10px; text-transform: uppercase; padding: 2px 7px; border-radius: 20px; background: #eef0f4; color: #8a90a0; }
      .badge.on { background: #e6f2ea; color: #177245; }
      .act { margin-left: auto; font-size: 11.5px; border: 1px solid #dfe1e8; background: #fff; border-radius: 7px; padding: 3px 10px; cursor: pointer; color: #565c6b; font-family: inherit; }
      .act:hover { border-color: #4038c4; color: #4038c4; }
      .act.ok:hover { border-color: #177245; color: #177245; }
      .prov { display: flex; align-items: center; font-size: 13px; padding: 4px 0; }
      .dot { width: 8px; height: 8px; border-radius: 50%; background: #177245; display: inline-block; margin-right: 8px; }
      .dot.down { background: #b3261e; }
      .empty { font-size: 13px; color: #a0a4b0; }
      .foot { font-size: 12px; color: #a0a4b0; margin: 4px 0 0; }
    `,
  ],
})
export class ConsoleComponent implements OnInit, OnDestroy {
  intervalSec = 4;
  obs: any = null;
  cost: any = null;
  prompts: any[] | null = null;
  providers: any[] | null = null;
  approvals: any[] | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.refresh();
    this.timer = setInterval(() => this.refresh(), this.intervalSec * 1000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  refresh(): void {
    const get = (u: string) => this.http.get<any>(u).pipe(catchError(() => of(null)));
    get('/api/observability').subscribe((d) => (this.obs = d));
    get('/api/cost').subscribe((d) => (this.cost = d));
    get('/api/prompts').subscribe((d) => (this.prompts = Array.isArray(d) ? d : d?.versions ?? null));
    get('/api/providers').subscribe((d) => (this.providers = d?.providers ?? null));
    get('/api/approvals').subscribe((d) => (this.approvals = d?.pending ?? null));
  }

  approve(id: string): void {
    this.http.post('/api/approvals/' + id + '/approve', {}).subscribe({
      next: () => this.refresh(),
      error: () => this.refresh(),
    });
  }

  activate(version: string): void {
    this.http.post('/api/prompts/' + version + '/activate', {}).subscribe({
      next: () => this.refresh(),
      error: () => this.refresh(),
    });
  }

  isNum(v: unknown): boolean {
    return typeof v === 'number';
  }

  num(v: unknown): string | number {
    return typeof v === 'number' ? v : '—';
  }

  money(v: unknown): string {
    return typeof v === 'number' ? '$' + v.toFixed(2) : '—';
  }

  budgetPct(): number | null {
    if (typeof this.cost?.today_usd !== 'number' || typeof this.cost?.budget_usd !== 'number' || !this.cost.budget_usd) return null;
    return Math.min(100, Math.round((this.cost.today_usd / this.cost.budget_usd) * 100));
  }
}
