import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

/**
 * Üst çubuktaki (topbar) arama kutusu ile pano gibi sayfalar arasında paylaşılan,
 * çok basit bir arama terimi deposu. Şimdilik sadece Pano ekranındaki görevleri
 * filtrelemek için kullanılıyor.
 */
@Injectable({
  providedIn: 'root'
})
export class SearchService {
  private termSubject = new BehaviorSubject<string>('');
  term$ = this.termSubject.asObservable();

  setTerm(value: string): void {
    this.termSubject.next(value);
  }

  clear(): void {
    this.termSubject.next('');
  }
}
