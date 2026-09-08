import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TaskAttachment } from './task';

/**
 * Göreve dosya ekleme. Yorumlar yalnızca düz metin olduğu için ekran görüntüsü, log ya da
 * doküman paylaşmak mümkün değildi.
 */
@Injectable({ providedIn: 'root' })
export class AttachmentService {
  private apiUrl = 'https://localhost:7236/api/tasks';

  constructor(private http: HttpClient) { }

  getAttachments(taskId: number): Observable<TaskAttachment[]> {
    return this.http.get<TaskAttachment[]>(`${this.apiUrl}/${taskId}/attachments`);
  }

  upload(taskId: number, file: File): Observable<TaskAttachment> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<TaskAttachment>(`${this.apiUrl}/${taskId}/attachments`, form);
  }

  delete(taskId: number, id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${taskId}/attachments/${id}`);
  }

  /**
   * Dosyanın içeriğini indirir. Ekler artık statik dosya olarak sunulmuyor (aksi halde
   * adresi bilen herkes, hiç giriş yapmamış biri bile indirebiliyordu); yetkili uçtan
   * geçtiği için isteğin Authorization başlığını taşıması gerekiyor. <img src> ve
   * <a href> bu başlığı gönderemediğinden içeriği blob olarak çekip tarayıcıda geçici
   * bir adrese bağlıyoruz.
   */
  download(taskId: number, id: number): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${taskId}/attachments/${id}/download`, {
      responseType: 'blob'
    });
  }
}

/**
 * Blob'u kullanıcının bilgisayarına indirtir. Sunucu adresini doğrudan bağlantı olarak
 * veremiyoruz (yetki başlığı gitmez), bu yüzden geçici bir nesne adresi üretip tıklıyoruz.
 */
export function saveBlobAs(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = objectUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  // Tarayıcının indirmeyi başlatmasına zaman tanıyıp adresi serbest bırakıyoruz.
  setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
}

/** Dosya boyutunu okunabilir hale getirir: 1536 → "1,5 KB". */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1).replace('.', ',')} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}
